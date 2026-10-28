using System.Net;
using System.Text;

namespace Reacher.Ingestion.Tests.Scb;

/// <summary>
/// Stands in for the network (hard rule 8). Answers requests in order and records
/// what was sent. A request with no answer left fails the test.
/// </summary>
internal sealed class FakeHttpHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] answers)
    : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _answers = new(answers);

    /// <summary>Path and query of every request, e.g. <c>/v1/kodtabeller/anstklkoder</c>.</summary>
    public List<string> Paths { get; } = [];

    /// <summary>The <c>X-API-Key</c> header of every request.</summary>
    public List<string?> ApiKeys { get; } = [];

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Status(HttpStatusCode status) => new(status);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Paths.Add(request.RequestUri!.PathAndQuery);
        ApiKeys.Add(request.Headers.TryGetValues("X-API-Key", out var keys) ? keys.Single() : null);

        if (_answers.Count == 0)
        {
            throw new InvalidOperationException($"Unexpected request {Paths.Count}.");
        }

        return Task.FromResult(_answers.Dequeue()(request));
    }
}
