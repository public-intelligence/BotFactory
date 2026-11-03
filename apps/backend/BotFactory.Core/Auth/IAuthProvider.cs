namespace BotFactory.Core.Auth;

public interface IAuthProvider
{
    Task<TokenResult> ExchangeCodeAsync(string code, string redirectUri, int organizationApplicationId);
    Task<TokenResult> RefreshAsync(string refreshToken);
    string GetLoginUrl(string redirectUri);
}
