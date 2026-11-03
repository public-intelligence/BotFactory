using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using BotFactory.Api.Logging;
using BotFactory.Core.DTOs;
using BotFactory.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace BotFactory.Tests;

[Collection("Database")]
public class SystemLogApiTests : IAsyncLifetime
{
    private readonly BotFactoryWebApplicationFactory _factory;
    private string _logDir = null!;
    private string _logPath = null!;

    public SystemLogApiTests(BotFactoryWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync()
    {
        _logDir = Path.Combine(Path.GetTempPath(), $"bf-logs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_logDir);
        _logPath = Path.Combine(_logDir, FileLog.FileNamePrefix + ".clef");
        File.WriteAllText(_logPath, BuildClefLine("Information", "startup complete", "Startup") + "\n");
        File.AppendAllText(_logPath, BuildClefLine("Error", "database timeout", "DbContext") + "\n");
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_logDir))
            Directory.Delete(_logDir, recursive: true);
        return Task.CompletedTask;
    }

    private WebApplicationFactory<Program> FactoryWithLogs() =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("FileLogging:Directory", _logDir);
        });

    private static string BuildClefLine(string level, string message, string category) =>
        $$"""{"@t":"2026-08-06T12:00:00Z","@l":"{{level}}","@m":"{{message}}","SourceContext":"{{category}}"}""";

    [Fact]
    public async Task SuperAdmin_CanTailLogs_WithLevelFilter()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(FactoryWithLogs());
        var tail = await client.GetFromJsonAsync<SystemLogTailResponse>("/api/logs?take=50&level=Error");

        Assert.NotNull(tail);
        Assert.Single(tail.Entries);
        Assert.Equal("Error", tail.Entries[0].Level);
        Assert.Contains("database timeout", tail.Entries[0].Message);
    }

    [Fact]
    public async Task BotAdmin_CanReadLogs()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(FactoryWithLogs(), "admin@example.com");
        var response = await client.GetAsync("/api/logs");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Analyst_ForbiddenOnLogs()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(FactoryWithLogs(), "analyst@example.com");
        var response = await client.GetAsync("/api/logs");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RawDownload_ReturnsFileContents()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(FactoryWithLogs());
        var raw = await client.GetStringAsync("/api/logs/raw?download=true");
        Assert.Contains("startup complete", raw);
        Assert.Contains("database timeout", raw);
    }

    [Fact]
    public async Task UploadSupportLogs_ForwardsToStubDropbox()
    {
        var capturedAuth = "";
        var factory = FactoryWithLogs().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddHttpClient("support-dropbox")
                    .ConfigurePrimaryHttpMessageHandler(() => new StubDropboxHandler(req =>
                    {
                        capturedAuth = req.Headers.Authorization?.ToString() ?? "";
                        return new HttpResponseMessage(HttpStatusCode.Created)
                        {
                            Content = new StringContent("""{"id":"drop-123"}""", Encoding.UTF8, "application/json"),
                        };
                    }));
            });
        });

        using var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/logs/upload");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "support-write-key");
        request.Content = new StringContent("bundle-bytes", Encoding.UTF8, "application/octet-stream");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Bearer support-write-key", capturedAuth);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("drop-123", body);
    }

    [Fact]
    public async Task UploadSupportLogs_MissingAuth_ReturnsUnauthorized()
    {
        using var client = FactoryWithLogs().CreateClient();
        var response = await client.PostAsync("/api/logs/upload", new StringContent("x"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed class StubDropboxHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubDropboxHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_responder(request));
    }
}
