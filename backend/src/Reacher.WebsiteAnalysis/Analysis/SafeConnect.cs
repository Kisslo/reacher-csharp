using System.Net;
using System.Net.Sockets;

namespace Reacher.WebsiteAnalysis.Analysis;

/// <summary>
/// Connect callback for our own page fetch. DNS is checked again here, then the
/// socket's remote address is checked, so a lookup that changes between the first
/// validation and the connect is still refused.
/// </summary>
public static class SafeConnect
{
    public static async ValueTask<Stream> ConnectAsync(
        IDnsLookup dns,
        DnsEndPoint endpoint,
        CancellationToken cancellationToken)
    {
        var addresses = await UrlSafety.ResolvePublicAddressesAsync(
            dns,
            endpoint.Host,
            cancellationToken);
        var address = addresses[0];
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            await socket.ConnectAsync(address, endpoint.Port, cancellationToken);
            if (socket.RemoteEndPoint is not IPEndPoint remote
                || IpAddressSafety.IsBlocked(remote.Address))
            {
                throw AnalysisException.InvalidUrl();
            }

            return new NetworkStream(socket, ownsSocket: true);
        }
        catch (AnalysisException)
        {
            socket.Dispose();
            throw;
        }
        catch (OperationCanceledException)
        {
            socket.Dispose();
            throw;
        }
        catch
        {
            socket.Dispose();
            throw AnalysisException.FetchFailed();
        }
    }
}
