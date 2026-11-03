namespace BotFactory.Core.DTOs;

public record OrgBrandingResponse(
    string? LogoFilename,
    bool HasLogo,
    string? LogoVersion,
    string? FaviconFilename,
    bool HasFavicon,
    string? FaviconVersion);
