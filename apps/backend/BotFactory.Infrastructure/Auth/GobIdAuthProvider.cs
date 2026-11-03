using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BotFactory.Core.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace BotFactory.Infrastructure.Auth;

public class GobIdAuthProvider : IAuthProvider
{
    private readonly HttpClient _httpClient;
    private readonly GobIdOptions _options;
    private readonly ILogger<GobIdAuthProvider> _logger;

    public GobIdAuthProvider(HttpClient httpClient, IOptions<GobIdOptions> options, ILogger<GobIdAuthProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<TokenResult> ExchangeCodeAsync(string code, string redirectUri, int organizationApplicationId)
    {
        var tokenEndpoint = $"{_options.ApiUrl}/api/v1/auth/token";
        _logger.LogInformation("GOB-ID ExchangeCode: calling {Endpoint}, orgAppId={OrgAppId}", tokenEndpoint, organizationApplicationId);

        var response = await _httpClient.PostAsJsonAsync(tokenEndpoint, new { code, redirectUri, organizationApplicationId });
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("GOB-ID ExchangeCode: {StatusCode} from {Endpoint}. Body: {ResponseBody}",
                (int)response.StatusCode, tokenEndpoint, responseBody);
            response.EnsureSuccessStatusCode();
        }

        var accessToken = ReadAccessTokenFromBody(responseBody);
        var refreshToken = ExtractRefreshTokenFromCookie(response);

        _logger.LogInformation("GOB-ID ExchangeCode: success. accessToken={TokenLength} chars, hasRefreshToken={HasRefresh}",
            accessToken.Length, refreshToken is not null);

        return new TokenResult(accessToken, refreshToken);
    }

    public async Task<TokenResult> RefreshAsync(string refreshToken)
    {
        var refreshEndpoint = $"{_options.ApiUrl}/api/auth/refresh";
        var request = new HttpRequestMessage(HttpMethod.Post, refreshEndpoint);
        request.Headers.Add("Cookie", $"refreshToken={refreshToken}");

        var response = await _httpClient.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("GOB-ID Refresh: {StatusCode} from {Endpoint}. Body: {ResponseBody}",
                (int)response.StatusCode, refreshEndpoint, responseBody);
            response.EnsureSuccessStatusCode();
        }

        var accessToken = ReadAccessTokenFromBody(responseBody);
        var newRefreshToken = ExtractRefreshTokenFromCookie(response);

        _logger.LogInformation("GOB-ID Refresh: success. accessToken={TokenLength} chars, hasRefreshToken={HasRefresh}",
            accessToken.Length, newRefreshToken is not null);

        return new TokenResult(accessToken, newRefreshToken);
    }

    public string GetLoginUrl(string redirectUri)
    {
        return $"{_options.LoginBaseUrl}/app/{_options.ApplicationId}/login";
    }

    private static string ReadAccessTokenFromBody(string responseBody)
    {
        GobIdTokenResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<GobIdTokenResponse>(responseBody);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"GOB-ID response is not valid JSON: {responseBody[..Math.Min(200, responseBody.Length)]}", ex);
        }

        return response?.AccessToken
            ?? throw new InvalidOperationException($"GOB-ID response missing accessToken. Body: {responseBody[..Math.Min(200, responseBody.Length)]}");
    }

    private record GobIdTokenResponse(
        [property: JsonPropertyName("accessToken")] string? AccessToken,
        [property: JsonPropertyName("mustChangePassword")] bool MustChangePassword = false
    );

    private string? ExtractRefreshTokenFromCookie(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookieHeaders))
        {
            _logger.LogWarning("GOB-ID: no Set-Cookie header in response — refresh token will be null");
            return null;
        }

        foreach (var cookieHeader in cookieHeaders)
        {
            if (SetCookieHeaderValue.TryParse(cookieHeader, out var parsedCookie)
                && parsedCookie.Name.Value == "refreshToken")
            {
                _logger.LogDebug("GOB-ID: extracted refreshToken from Set-Cookie header");
                return parsedCookie.Value.Value;
            }
        }

        _logger.LogWarning("GOB-ID: Set-Cookie header present but no 'refreshToken' cookie found. Headers: {Headers}",
            string.Join("; ", cookieHeaders));
        return null;
    }
}
