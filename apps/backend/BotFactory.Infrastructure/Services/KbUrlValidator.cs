using System.Net;
using System.Net.Sockets;

namespace BotFactory.Infrastructure.Services;

public static class KbUrlValidator
{
    public const int MaxUrlsPerRequest = 5000;

    public static bool TryNormalize(string? raw, out string normalized, out string? error)
    {
        normalized = string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "URL is empty.";
            return false;
        }

        var trimmed = raw.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            error = $"Invalid URL: {trimmed}";
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            error = $"Only http and https URLs are allowed: {trimmed}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            error = $"URL host is missing: {trimmed}";
            return false;
        }

        if (IsBlockedHost(uri.Host))
        {
            error = $"URL host is not allowed: {uri.Host}";
            return false;
        }

        // Prefer the absolute URI without fragment.
        var builder = new UriBuilder(uri) { Fragment = string.Empty };
        normalized = builder.Uri.AbsoluteUri;
        return true;
    }

    public static async Task<(bool Ok, string? Error)> IsSafeToFetchAsync(Uri uri, CancellationToken ct = default)
    {
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return (false, "Only http and https URLs are allowed.");

        if (IsBlockedHost(uri.Host))
            return (false, $"URL host is not allowed: {uri.Host}");

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, ct);
        }
        catch (Exception ex)
        {
            return (false, $"Could not resolve host: {ex.Message}");
        }

        if (addresses.Length == 0)
            return (false, "Host resolved to no addresses.");

        foreach (var address in addresses)
        {
            if (IsBlockedAddress(address))
                return (false, $"URL resolves to a blocked address: {address}");
        }

        return (true, null);
    }

    private static bool IsBlockedHost(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "metadata.google.internal", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(host, out var ip) && IsBlockedAddress(ip);
    }

    private static bool IsBlockedAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            // 10.0.0.0/8
            if (bytes[0] == 10) return true;
            // 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168) return true;
            // 169.254.0.0/16 link-local + cloud metadata
            if (bytes[0] == 169 && bytes[1] == 254) return true;
            // 127.0.0.0/8 already covered by IsLoopback, but be explicit
            if (bytes[0] == 127) return true;
            // 0.0.0.0/8
            if (bytes[0] == 0) return true;
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal)
                return true;
            if (address.IsIPv4MappedToIPv6)
                return IsBlockedAddress(address.MapToIPv4());
        }

        return false;
    }
}
