using System.Net;
using System.Net.Sockets;

namespace Reacher.WebsiteAnalysis.Analysis;

/// <summary>
/// Decides whether an address is safe to connect to. Blocks loopback, private,
/// link-local, CGNAT, multicast, reserved and benchmarking IPv4 ranges, and the
/// reserved <c>0000::/8</c> (incl. <c>::</c>, <c>::1</c> and NAT64), link-local,
/// site-local, unique local, multicast and 6to4 IPv6 ranges. IPv4-mapped IPv6 is
/// checked as IPv4. One blocked address rejects the whole host (fail closed).
/// Unknown address families are blocked too.
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
                198 => bytes[1] is 18 or 19, // 198.18.0.0/15 benchmarking
                >= 224 => true, // 224.0.0.0/4 multicast, 240.0.0.0/4 reserved incl. broadcast
                _ => false,
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal)
            {
                return true; // fe80::/10, fec0::/10
            }

            var bytes = address.GetAddressBytes();
            return bytes[0] switch
            {
                0x00 => true, // 0000::/8 reserved: ::, ::1, IPv4-compatible, NAT64 64:ff9b::/96
                0xFC or 0xFD => true, // fc00::/7 unique local
                0xFF => true, // ff00::/8 multicast
                0x20 => bytes[1] == 0x02, // 2002::/16 6to4
                _ => false,
            };
        }

        return true;
    }
}
