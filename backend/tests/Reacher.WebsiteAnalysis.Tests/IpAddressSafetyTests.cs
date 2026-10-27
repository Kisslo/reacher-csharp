using System.Net;
using Reacher.WebsiteAnalysis.Analysis;

namespace Reacher.WebsiteAnalysis.Tests;

public sealed class IpAddressSafetyTests
{
    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("10.0.0.1", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.31.255.255", true)]
    [InlineData("192.168.0.1", true)]
    [InlineData("169.254.169.254", true)]
    [InlineData("100.64.0.0", true)]
    [InlineData("100.127.255.255", true)]
    [InlineData("0.0.0.0", true)]
    [InlineData("100.128.0.1", false)]
    [InlineData("172.32.0.1", false)]
    [InlineData("203.0.113.10", false)]
    [InlineData("::1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("fc00::1", true)]
    [InlineData("fd12::1", true)]
    [InlineData("::ffff:10.0.0.1", true)]
    [InlineData("::ffff:203.0.113.10", false)]
    [InlineData("2001:db8::1", false)]
    public void Classifies_addresses(string text, bool blocked)
    {
        Assert.Equal(blocked, IpAddressSafety.IsBlocked(IPAddress.Parse(text)));
    }
}
