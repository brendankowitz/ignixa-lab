using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;

namespace Ignixa.Lab.Functions.Execution;

/// <summary>
/// Validates target FHIR server URLs before the backend makes outbound
/// requests. Prevents Server-Side Request Forgery (SSRF) by rejecting
/// non-HTTP(S) schemes and, unless explicitly permitted, addresses that
/// resolve to loopback, link-local, or private networks.
/// </summary>
public static class TargetUrlValidator
{
    /// <summary>
    /// Validates <paramref name="rawUrl"/> and, on success, returns the parsed
    /// <paramref name="uri"/>. On failure returns <see langword="false"/> with a
    /// human-readable <paramref name="error"/>.
    /// </summary>
    public static bool TryValidate(
        string? rawUrl,
        bool allowPrivateTargets,
        [NotNullWhen(true)] out Uri? uri,
        [NotNullWhen(false)] out string? error)
    {
        uri = null;
        error = null;

        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            error = "A target URL is required.";
            return false;
        }

        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var parsed))
        {
            error = "The target URL must be an absolute URL.";
            return false;
        }

        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
        {
            error = "The target URL must use http or https.";
            return false;
        }

        if (!allowPrivateTargets && IsBlockedHost(parsed))
        {
            error = "The target URL resolves to a private, loopback, or link-local address, which is not permitted.";
            return false;
        }

        uri = parsed;
        return true;
    }

    private static bool IsBlockedHost(Uri uri)
    {
        // Literal IP host.
        if (IPAddress.TryParse(uri.Host, out var literal))
        {
            return IsBlocked(literal);
        }

        // "localhost" and friends never leave the machine.
        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Resolve the DNS name; block if any resolved address is internal.
        try
        {
            var addresses = Dns.GetHostAddresses(uri.DnsSafeHost);
            return addresses.Length == 0 || addresses.Any(IsBlocked);
        }
        catch (SocketException)
        {
            // Unresolvable host: let the HTTP call surface the failure rather
            // than blocking outright, so users get a clear connection error.
            return false;
        }
    }

    private static bool IsBlocked(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();

            // 10.0.0.0/8
            if (bytes[0] == 10)
            {
                return true;
            }

            // 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            {
                return true;
            }

            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168)
            {
                return true;
            }

            // 169.254.0.0/16 (link-local)
            if (bytes[0] == 169 && bytes[1] == 254)
            {
                return true;
            }

            // 0.0.0.0/8 (this network)
            if (bytes[0] == 0)
            {
                return true;
            }

            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || address.IsIPv6UniqueLocal
                || IPAddress.IPv6Any.Equals(address);
        }

        return false;
    }
}
