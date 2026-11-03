namespace BotFactory.Core.Auth;

public record TokenResult(string AccessToken, string? RefreshToken);

public record TokenExchangeRequest(string Code, string RedirectUri, int OrganizationApplicationId);

public record LoginUrlResponse(string Url);

public record AuthenticatedResponse(bool Authenticated);

public record UserInfoResponse(
    string UserId,
    string FullName,
    string OrganizationId,
    string OrganizationName,
    IReadOnlyList<string> Permissions);
