using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Http;

namespace Ignixa.Lab.Functions.Middleware;

/// <summary>
/// Derives a stable rate-limiting partition key from the request's client IP.
/// App Service <em>appends</em> the true client IP to <c>X-Forwarded-For</c>, so
/// only the right-most entry is trustworthy (everything to its left is
/// client-supplied and spoofable). IPv6 clients are collapsed to their /64
/// prefix so rotating within a delegated prefix maps to one key. See ADR-2608 §3.
/// </summary>
public static class ClientIpKeyExtractor
{
    private const string ForwardedForHeader = "X-Forwarded-For";
    private const string UnknownKey = "unknown";

    public static string Extract(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var address = ResolveAddress(httpContext);
        return address is null ? UnknownKey : Normalize(address);
    }

    private static IPAddress? ResolveAddress(HttpContext httpContext)
    {
        var rightmost = RightmostForwardedFor(httpContext);
        if (rightmost is not null && TryParseHostEntry(rightmost, out var forwarded))
        {
            return forwarded;
        }

        return httpContext.Connection.RemoteIpAddress;
    }

    private static string? RightmostForwardedFor(HttpContext httpContext)
    {
        string? rightmost = null;
        foreach (var headerValue in httpContext.Request.Headers[ForwardedForHeader])
        {
            if (string.IsNullOrEmpty(headerValue))
            {
                continue;
            }

            foreach (var entry in headerValue.Split(','))
            {
                var trimmed = entry.Trim();
                if (trimmed.Length > 0)
                {
                    rightmost = trimmed;
                }
            }
        }

        return rightmost;
    }

    private static bool TryParseHostEntry(string entry, [NotNullWhen(true)] out IPAddress? address)
    {
        if (IPAddress.TryParse(entry, out var parsed))
        {
            address = parsed;
            return true;
        }

        // Bracketed IPv6, with or without a trailing :port — "[::1]:1234" / "[::1]".
        if (entry.StartsWith('['))
        {
            var close = entry.IndexOf(']');
            if (close > 1 && IPAddress.TryParse(entry.AsSpan(1, close - 1), out var bracketed))
            {
                address = bracketed;
                return true;
            }
        }

        // IPv4 with :port — "1.2.3.4:5678". A bare IPv6 has multiple colons but
        // already parsed above, so a lone trailing :port here is unambiguous.
        var lastColon = entry.LastIndexOf(':');
        if (lastColon > 0 && IPAddress.TryParse(entry.AsSpan(0, lastColon), out var ported))
        {
            address = ported;
            return true;
        }

        address = null;
        return false;
    }

    private static string Normalize(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        var bytes = address.GetAddressBytes();
        for (var i = 8; i < bytes.Length; i++)
        {
            bytes[i] = 0;
        }

        return new IPAddress(bytes).ToString();
    }
}
