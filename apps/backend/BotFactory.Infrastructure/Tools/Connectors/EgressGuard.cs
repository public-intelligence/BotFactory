using BotFactory.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace BotFactory.Infrastructure.Tools.Connectors;

/// <summary>
/// Options for outbound tool traffic. Production defaults are strict (https
/// only, no private networks); development overrides both so admins can test
/// against local mocks (appsettings.Development.json → "ToolEgress").
/// </summary>
public class ToolEgressOptions
{
    public const string SectionName = "ToolEgress";

    public bool AllowHttp { get; set; }
    public bool AllowPrivateNetworks { get; set; }
}

public interface IEgressGuard
{
    /// <summary>SSRF gate for a fully-resolved outbound URL. Runs at call time (DNS re-resolution included).</summary>
    Task<(bool Ok, string? Error)> ValidateAsync(Uri uri, CancellationToken cancellationToken);
}

/// <summary>
/// Reuses the KB scraper's SSRF checks (KbUrlValidator: loopback, private,
/// link-local, and cloud-metadata ranges blocked; DNS-resolved and re-checked)
/// and adds the https-only rule for tool egress.
/// </summary>
public class EgressGuard : IEgressGuard
{
    private readonly IOptions<ToolEgressOptions> _options;

    public EgressGuard(IOptions<ToolEgressOptions> options)
    {
        _options = options;
    }

    public async Task<(bool Ok, string? Error)> ValidateAsync(Uri uri, CancellationToken cancellationToken)
    {
        var opts = _options.Value;

        if (uri.Scheme != Uri.UriSchemeHttps && !(opts.AllowHttp && uri.Scheme == Uri.UriSchemeHttp))
            return (false, "Solo se permiten destinos https.");

        if (opts.AllowPrivateNetworks)
            return (true, null);

        return await KbUrlValidator.IsSafeToFetchAsync(uri, cancellationToken);
    }
}
