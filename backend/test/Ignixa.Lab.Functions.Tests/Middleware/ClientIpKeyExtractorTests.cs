using System.Net;
using FluentAssertions;
using Ignixa.Lab.Functions.Middleware;
using Microsoft.AspNetCore.Http;

namespace Ignixa.Lab.Functions.Tests.Middleware;

public sealed class ClientIpKeyExtractorTests
{
    [Fact]
    public void Extract_MultiHopForwardedFor_ChoosesRightmostEntry()
    {
        var context = ContextWithForwardedFor("203.0.113.5, 10.0.0.1, 198.51.100.9:54321");

        ClientIpKeyExtractor.Extract(context).Should().Be("198.51.100.9");
    }

    [Fact]
    public void Extract_Ipv4WithPort_StripsPort()
    {
        var context = ContextWithForwardedFor("203.0.113.7:443");

        ClientIpKeyExtractor.Extract(context).Should().Be("203.0.113.7");
    }

    [Fact]
    public void Extract_BracketedIpv6WithPort_NormalizesToSlash64Prefix()
    {
        var context = ContextWithForwardedFor("[2001:db8::1]:443");

        ClientIpKeyExtractor.Extract(context).Should().Be("2001:db8::");
    }

    [Fact]
    public void Extract_Ipv6AddressesSharingSlash64_ProduceSameKey()
    {
        var low = ContextWithForwardedFor("[2001:db8:0:0:1111:2222:3333:4444]:1");
        var high = ContextWithForwardedFor("[2001:db8:0:0:5555:6666:7777:8888]:2");

        ClientIpKeyExtractor.Extract(low).Should().Be(ClientIpKeyExtractor.Extract(high));
    }

    [Fact]
    public void Extract_NoForwardedForHeader_FallsBackToRemoteIp()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.50");

        ClientIpKeyExtractor.Extract(context).Should().Be("203.0.113.50");
    }

    [Fact]
    public void Extract_NoHeaderAndNoRemoteIp_ReturnsUnknown()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = null;

        ClientIpKeyExtractor.Extract(context).Should().Be("unknown");
    }

    private static DefaultHttpContext ContextWithForwardedFor(string value)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Forwarded-For"] = value;
        return context;
    }
}
