using System.Net;
using System.Text;
using BotFactory.Core.Auth;
using BotFactory.Infrastructure.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BotFactory.Tests;

public class GobIdAuthProviderTests
{
    private sealed class QueueHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _handlers = new();
        public List<HttpRequestMessage> Requests { get; } = [];

        public void Enqueue(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handlers.Enqueue(handler);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (_handlers.Count == 0)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            return Task.FromResult(_handlers.Dequeue()(request));
        }
    }

    private static GobIdAuthProvider Create(HttpMessageHandler handler) =>
        new(new HttpClient(handler), Options.Create(new GobIdOptions
        {
            ApiUrl = "https://gobid.test",
            Audience = "gobid-users",
            ApplicationId = "app-123",
            AppSecret = "secret",
            LoginBaseUrl = "https://login.gobid.test",
        }), NullLogger<GobIdAuthProvider>.Instance);

    [Fact]
    public async Task ExchangeCode_ReadsAccessTokenAndRefreshCookie()
    {
        var handler = new QueueHandler();
        handler.Enqueue(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"accessToken":"access.jwt.token","mustChangePassword":false}""", Encoding.UTF8, "application/json"),
            };
            response.Headers.TryAddWithoutValidation("Set-Cookie", "refreshToken=rt-abc; Path=/; HttpOnly");
            return response;
        });

        var provider = Create(handler);
        var result = await provider.ExchangeCodeAsync("auth-code", "http://localhost/cb", 42);

        Assert.Equal("access.jwt.token", result.AccessToken);
        Assert.Equal("rt-abc", result.RefreshToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://gobid.test/api/v1/auth/token", request.RequestUri!.ToString());
        var body = await request.Content!.ReadAsStringAsync();
        Assert.Contains("auth-code", body);
        Assert.Contains("42", body);
    }

    [Fact]
    public async Task ExchangeCode_MissingAccessToken_Throws()
    {
        var handler = new QueueHandler();
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"mustChangePassword":false}""", Encoding.UTF8, "application/json"),
        });

        var provider = Create(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ExchangeCodeAsync("code", "http://localhost/cb", 1));
    }

    [Fact]
    public async Task ExchangeCode_NonSuccess_Throws()
    {
        var handler = new QueueHandler();
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("nope"),
        });

        var provider = Create(handler);
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            provider.ExchangeCodeAsync("code", "http://localhost/cb", 1));
    }

    [Fact]
    public async Task ExchangeCode_NoSetCookie_ReturnsNullRefresh()
    {
        var handler = new QueueHandler();
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"accessToken":"tok"}""", Encoding.UTF8, "application/json"),
        });

        var provider = Create(handler);
        var result = await provider.ExchangeCodeAsync("code", "http://localhost/cb", 1);
        Assert.Equal("tok", result.AccessToken);
        Assert.Null(result.RefreshToken);
    }

    [Fact]
    public async Task Refresh_PostsCookieAndReadsNewTokens()
    {
        var handler = new QueueHandler();
        handler.Enqueue(req =>
        {
            Assert.Equal("https://gobid.test/api/auth/refresh", req.RequestUri!.ToString());
            Assert.True(req.Headers.TryGetValues("Cookie", out var cookies));
            Assert.Contains("refreshToken=old-rt", cookies);

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"accessToken":"new.access"}""", Encoding.UTF8, "application/json"),
            };
            response.Headers.TryAddWithoutValidation("Set-Cookie", "refreshToken=new-rt; Path=/");
            return response;
        });

        var provider = Create(handler);
        var result = await provider.RefreshAsync("old-rt");
        Assert.Equal("new.access", result.AccessToken);
        Assert.Equal("new-rt", result.RefreshToken);
    }

    [Fact]
    public void GetLoginUrl_UsesApplicationId()
    {
        var provider = Create(new QueueHandler());
        Assert.Equal("https://login.gobid.test/app/app-123/login", provider.GetLoginUrl("ignored"));
    }
}
