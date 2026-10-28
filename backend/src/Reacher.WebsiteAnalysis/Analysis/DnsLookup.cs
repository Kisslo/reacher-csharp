using System.Net;

namespace Reacher.WebsiteAnalysis.Analysis;

/// <summary>
/// Resolves a host name. Tests supply a fake so CI never queries DNS.
/// </summary>
public interface IDnsLookup
{
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken);
}

public sealed class SystemDnsLookup : IDnsLookup
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
        Dns.GetHostAddressesAsync(host, cancellationToken);
}
