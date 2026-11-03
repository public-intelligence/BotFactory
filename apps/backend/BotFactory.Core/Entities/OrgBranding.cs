namespace BotFactory.Core.Entities;

/// <summary>
/// Per-organization branding assets (logo, favicon). One row per org.
/// </summary>
public class OrgBranding : BaseEntity, IAuditable
{
    public string? LogoBlobKey { get; set; }
    public string? LogoFilename { get; set; }
    public string? FaviconBlobKey { get; set; }
    public string? FaviconFilename { get; set; }
}
