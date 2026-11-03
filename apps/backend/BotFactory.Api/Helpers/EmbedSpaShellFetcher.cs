using System.Net;
using System.Net.Sockets;

namespace BotFactory.Api.Helpers;

/// <summary>
/// Loads the SPA <c>index.html</c> for <c>/api/embed/{publicId}</c>.
/// In Docker stacks the public hostname often does not hairpin back to this
/// host, so we prefer fetching via the compose frontend container
/// (<c>bf-frontend:443</c>) with the browser-facing Host/SNI, then fall back
/// to <see cref="Core.Auth.PublicAccessOptions.SpaIndexUrl"/> and wwwroot.
/// </summary>
public static class EmbedSpaShellFetcher
{
    /// <summary>Compose <c>container_name</c> for the SPA/nginx service.</summary>
    public const string DockerFrontendHost = "bf-frontend";

    private static readonly TimeSpan DnsProbeTimeout = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Absolute HTTPS URL of index.html to request (Host/SNI), or null if none
    /// can be derived from config / the incoming embed request.
    /// Prefers a configured https SpaIndexUrl; when that is missing, http-only,
    /// or points at the Docker service name, uses the embed request host.
    /// </summary>
    public static string? ResolveShellUrl(string? spaIndexUrl, string? requestHost)
    {
        if (Uri.TryCreate(spaIndexUrl, UriKind.Absolute, out var configured)
            && configured.Scheme == Uri.UriSchemeHttps
            && !IsDockerFrontendHost(configured.Host))
        {
            return spaIndexUrl;
        }

        var host = requestHost?.Trim();
        if (!string.IsNullOrEmpty(host)
            && !IsDockerFrontendHost(host)
            && Uri.TryCreate($"https://{host}/index.html", UriKind.Absolute, out var fromRequest))
        {
            return fromRequest.AbsoluteUri;
        }

        return null;
    }

    public static bool IsDockerFrontendHost(string? host) =>
        string.Equals(host, DockerFrontendHost, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when the compose frontend hostname resolves (Docker DNS). Failures
    /// and timeouts are treated as "not in Docker" so Azure/SWA stays on the
    /// public URL path with only a short DNS miss.
    /// </summary>
    public static async Task<bool> IsDockerFrontendReachableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(DnsProbeTimeout);
            var addresses = await Dns.GetHostAddressesAsync(DockerFrontendHost, cts.Token);
            return addresses.Length > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static async Task<string?> FetchAsync(
        string? spaIndexUrl,
        string? requestHost,
        IHttpClientFactory httpClientFactory,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var shellUrl = ResolveShellUrl(spaIndexUrl, requestHost);

        if (shellUrl is not null && await IsDockerFrontendReachableAsync(cancellationToken))
        {
            try
            {
                var html = await FetchViaDockerFrontendAsync(shellUrl, cancellationToken);
                if (!string.IsNullOrEmpty(html))
                {
                    logger.LogDebug("Embed: SPA shell loaded via {Frontend} for {Url}", DockerFrontendHost, shellUrl);
                    return html;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Embed: failed to fetch SPA index via {Frontend} for {Url}",
                    DockerFrontendHost, shellUrl);
            }
        }

        if (!string.IsNullOrEmpty(spaIndexUrl))
        {
            try
            {
                var client = httpClientFactory.CreateClient("embed-shell");
                var html = await client.GetStringAsync(spaIndexUrl, cancellationToken);
                if (!string.IsNullOrEmpty(html))
                    return html;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Embed: failed to fetch SPA index from {Url}", spaIndexUrl);
            }
        }

        return null;
    }

    /// <summary>
    /// GET <paramref name="shellUrl"/> over TLS, but open the TCP connection to
    /// <see cref="DockerFrontendHost"/>:443 so we never need the public VIP to
    /// hairpin. Certificate validation still uses the URL host (SNI).
    /// </summary>
    internal static async Task<string> FetchViaDockerFrontendAsync(
        string shellUrl,
        CancellationToken cancellationToken)
    {
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (context, ct) =>
            {
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(DockerFrontendHost, 443, ct);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };

        using var client = new HttpClient(handler) { Timeout = FetchTimeout };
        using var response = await client.GetAsync(shellUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
}
