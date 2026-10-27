using System.Net;

namespace Reacher.WebsiteAnalysis.Analysis;

/// <summary>
/// Rejects URLs the server must not fetch (D10). Call this before every request,
/// including each redirect hop.
/// </summary>
public sealed class UrlSafety
{
    private readonly IDnsLookup _dns;

    public UrlSafety(IDnsLookup dns)
    {
        _dns = dns;
    }

    public async Task<Uri> ValidateAsync(string? url, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw AnalysisException.InvalidUrl();
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw AnalysisException.InvalidUrl();
        }

        if (!string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort || uri.IdnHost.Length == 0)
        {
            throw AnalysisException.InvalidUrl();
        }

        await ResolvePublicAddressesAsync(_dns, uri.IdnHost, cancellationToken);
        return uri;
    }

    /// <summary>
    /// Returns every address for the host, or throws <see cref="AnalysisException"/>
    /// with <see cref="AnalysisException.InvalidUrlCode"/> when any address is blocked,
    /// none were found, or resolution failed. IP literals are not sent to DNS.
    /// </summary>
    public static async Task<IPAddress[]> ResolvePublicAddressesAsync(
        IDnsLookup dns,
        string host,
        CancellationToken cancellationToken)
    {
        if (host.Length >= 2 && host[0] == '[' && host[^1] == ']')
        {
            host = host[1..^1];
        }

        if (host.Length == 0)
        {
            throw AnalysisException.InvalidUrl();
        }

        IPAddress[] addresses;
        if (IPAddress.TryParse(host, out var literal))
        {
            addresses = [literal];
        }
        else
        {
            try
            {
                addresses = await dns.ResolveAsync(host, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // SocketException messages include the host. Drop them (D16).
                throw AnalysisException.InvalidUrl();
            }
        }

        if (addresses.Length == 0 || Array.Exists(addresses, IpAddressSafety.IsBlocked))
        {
            throw AnalysisException.InvalidUrl();
        }

        return addresses;
    }
}
