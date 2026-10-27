using System.Diagnostics;
using System.Net;
using System.Text;

namespace Reacher.WebsiteAnalysis.Analysis;

/// <summary>
/// Fetches one page for our own checks. Redirects are followed by hand, at most
/// <see cref="MaxRedirects"/>, and every hop is validated again. The body is capped
/// at <see cref="MaxBodyBytes"/>.
/// </summary>
public sealed class WebsitePageFetcher
{
    public const int TimeoutSeconds = 10;

    public const int MaxBodyBytes = 2 * 1024 * 1024;

    public const int MaxRedirects = 5;

    private readonly HttpClient _http;

    private readonly UrlSafety _safety;

    private readonly TimeProvider _time;

    public WebsitePageFetcher(HttpClient http, UrlSafety safety, TimeProvider time)
    {
        _http = http;
        _safety = safety;
        _time = time;
    }

    public async Task<PageFetch> FetchAsync(Uri url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);
        try
        {
            return await FetchCoreAsync(url, linked.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw AnalysisException.Timeout();
        }
    }

    private async Task<PageFetch> FetchCoreAsync(Uri url, CancellationToken cancellationToken)
    {
        var current = await _safety.ValidateAsync(Absolute(url), cancellationToken);
        var followed = 0;
        while (true)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            var started = Stopwatch.GetTimestamp();
            using var response = await SendAsync(request, cancellationToken);
            if (IsRedirect(response.StatusCode))
            {
                if (followed == MaxRedirects)
                {
                    throw AnalysisException.InvalidUrl();
                }

                followed++;
                current = await NextHopAsync(current, response, cancellationToken);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw AnalysisException.FetchFailed();
            }

            var responseTimeMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var html = await ReadHtmlAsync(response, cancellationToken);
            var findings = HtmlPageChecks.Read(html, current, _time);
            var checks = new WebsiteChecks(
                findings.Https,
                findings.Viewport,
                findings.Generator,
                findings.CopyrightYear,
                responseTimeMs);
            return new PageFetch(current, checks);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (AnalysisException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            throw AnalysisException.FetchFailed();
        }
    }

    private async Task<Uri> NextHopAsync(
        Uri current,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Headers.Location is not Uri location)
        {
            throw AnalysisException.InvalidUrl();
        }

        Uri next;
        try
        {
            next = location.IsAbsoluteUri ? location : new Uri(current, location);
        }
        catch (UriFormatException)
        {
            throw AnalysisException.InvalidUrl();
        }

        return await _safety.ValidateAsync(next.AbsoluteUri, cancellationToken);
    }

    private static string Absolute(Uri url)
    {
        try
        {
            return url.AbsoluteUri;
        }
        catch (InvalidOperationException)
        {
            throw AnalysisException.InvalidUrl();
        }
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Redirect
            or HttpStatusCode.RedirectMethod
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    private static async Task<string> ReadHtmlAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var capped = new CappedStream(stream, MaxBodyBytes);
        using var reader = new StreamReader(
            capped,
            ChooseEncoding(response),
            detectEncodingFromByteOrderMarks: true,
            leaveOpen: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static Encoding ChooseEncoding(HttpResponseMessage response)
    {
        var charset = response.Content.Headers.ContentType?.CharSet;
        if (string.IsNullOrWhiteSpace(charset))
        {
            return Encoding.UTF8;
        }

        try
        {
            return Encoding.GetEncoding(charset.Trim().Trim('"'));
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8;
        }
    }

    /// <summary>
    /// Stops reading after a fixed number of bytes. The response still owns the
    /// inner stream.
    /// </summary>
    private sealed class CappedStream : Stream
    {
        private readonly Stream _inner;

        private int _remaining;

        public CappedStream(Stream inner, int maxBytes)
        {
            _inner = inner;
            _remaining = maxBytes;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_remaining == 0)
            {
                return 0;
            }

            var allowed = Math.Min(count, _remaining);
            var read = _inner.Read(buffer, offset, allowed);
            _remaining -= read;
            return read;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_remaining == 0)
            {
                return 0;
            }

            var allowed = Math.Min(buffer.Length, _remaining);
            var read = await _inner.ReadAsync(buffer[..allowed], cancellationToken);
            _remaining -= read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
