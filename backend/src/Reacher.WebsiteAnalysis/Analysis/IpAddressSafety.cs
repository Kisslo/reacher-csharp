using System.Net;
using System.Net.Sockets;

namespace Reacher.WebsiteAnalysis.Analysis;

/// <summary>
/// Decides whether an address is safe to connect to. One blocked address rejects
/// the whole host (fail closed). Unknown address families are blocked too.
/// </summary>
public static class IpAddressSafety
{
    public static bool IsBlocked(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] switch
            {
                0 => true, // 0.0.0.0/8
                10 => true, // 10.0.0.0/8
                100 => bytes[1] is >= 64 and <= 127, // 100.64.0.0/10 CGNAT
                127 => true, // 127.0.0.0/8
                169 => bytes[1] == 254, // 169.254.0.0/16
                172 => bytes[1] is >= 16 and <= 31, // 172.16.0.0/12
                192 => bytes[1] == 168, // 192.168.0.0/16
                _ => false,
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal)
            {
                return true; // fe80::/10
            }

            var bytes = address.GetAddressBytes();
            return (bytes[0] & 0xFE) == 0xFC; // fc00::/7 unique local
        }

        return true;
    }
}
