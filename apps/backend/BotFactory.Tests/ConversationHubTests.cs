using System.Net;
using System.Net.Http.Json;
using System.Text;
using BotFactory.Core;
using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using BotFactory.Core.Tenancy;
using BotFactory.Core.Tools;
using BotFactory.Infrastructure.Data;
using BotFactory.Infrastructure.Services;
using BotFactory.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BotFactory.Tests;

[Collection("LiveKitTurn")]
public class ConversationHubTests
{
    private readonly LiveKitWebApplicationFactory _factory;

    public ConversationHubTests(LiveKitWebApplicationFactory factory) => _factory = factory;

    private static WebApplicationFactory<Program> WithTurnGemini(
        LiveKitWebApplicationFactory baseFactory,
        HttpMessageHandler geminiHandler,
        string fallbackMessage = "Lo siento, no pude procesar eso.")
    {
        return baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IOptions<GeminiChatOptions>>();
                services.AddSingleton(Options.Create(new GeminiChatOptions
                {
                    MaxToolIterations = 2,
                    FallbackMessage = fallbackMessage,
                    RetryDelayMilliseconds = 0,
                }));

                services.RemoveAll<GeminiChatService>();
                services.AddScoped<GeminiChatService>(sp =>
                {
                    var http = new HttpClient(geminiHandler) { Timeout = TimeSpan.FromSeconds(30) };
                    return new GeminiChatService(
                        http,
                        sp.GetRequiredService<IAppSettingService>(),
                        sp.GetRequiredService<IOptions<GeminiChatOptions>>(),
                        sp.GetRequiredService<ILogger<GeminiChatService>>(),
                        sp.GetRequiredService<IToolRegistry>());
                });
            });
        });
    }

    private sealed class SseQueueHandler : HttpMessageHandler
    {
        private readonly Queue<string> _streamBodies = new();
        public void EnqueueStream(string sseBody) => _streamBodies.Enqueue(sseBody);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.Contains("streamGenerateContent", StringComparison.Ordinal))
            {
                var body = _streamBodies.Count > 0
                    ? _streamBodies.Dequeue()
                    : "data: {\"candidates\":[]}\n\n";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class GeminiHttpErrorHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.Contains("streamGenerateContent", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(status)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static async Task<string> MintHubTokenAsync(WebApplicationFactory<Program> app)
    {
        var client = app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var response = await client.PostAsJsonAsync("/api/auth/dev/token", new
        {
            email = "superadmin@example.com",
            organizationApplicationId = 1,
        });
        response.EnsureSuccessStatusCode();
        return await client.GetStringAsync("/api/auth/hub-token");
    }

    private static async Task<(int ConversationId, string HubToken)> SeedOwnedConversationAsync(
        WebApplicationFactory<Program> app,
        Action<Bot>? configureBot = null)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
        tenant.SetTenant(DbSeeder.SuperAdminOrgId);

        var bot = new Bot
        {
            BotName = "Hub Test Bot",
            UrlSlug = $"hub-{Guid.NewGuid():N}",
            PublicId = PublicIdGenerator.NewId(),
            PublishedState = BotStates.Published,
            MasterPrompt = "You are a hub test bot.",
            EnableChat = true,
            EnableVoice = false,
        };
        configureBot?.Invoke(bot);
        db.Bots.Add(bot);
        await db.SaveChangesAsync();

        const string userId = "00000000-0000-0000-0000-000000000001";
        var conv = new Conversation
        {
            BotId = bot.Id,
            UserId = userId,
            Status = ConversationStatus.Active,
        };
        db.Conversations.Add(conv);
        await db.SaveChangesAsync();

        var token = await MintHubTokenAsync(app);
        return (conv.Id, token);
    }

    [Fact]
    public async Task SendTurn_HappyPath_EmitsStartedDeltaCompleted()
    {
        var handler = new SseQueueHandler();
        handler.EnqueueStream(
            """
            data: {"candidates":[{"content":{"parts":[{"text":"Hola hub"}]}}]}

            """);

        using var app = WithTurnGemini(_factory, handler);
        var (convId, token) = await SeedOwnedConversationAsync(app);

        await using var hub = await HubTestHelper.StartAsync(app, token);
        await hub.InvokeAsync("SubscribeConversation", convId);

        var started = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        var delta = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);

        hub.On<object>("turnStarted", payload =>
        {
            var turnId = ReadGuid(payload, "turnId");
            started.TrySetResult(turnId);
        });
        hub.On<object>("turnDelta", payload =>
        {
            var text = ReadString(payload, "text");
            delta.TrySetResult(text);
        });
        hub.On<object>("turnCompleted", payload =>
        {
            var turnId = ReadGuid(payload, "turnId");
            completed.TrySetResult(turnId);
        });

        await hub.InvokeAsync("SendTurn", convId, "Hola");

        var turnId = await started.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var text = await delta.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var done = await completed.Task.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Equal(turnId, done);
        Assert.Contains("Hola hub", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendTurn_EmptyText_ThrowsHubException()
    {
        using var app = WithTurnGemini(_factory, new SseQueueHandler());
        var (convId, token) = await SeedOwnedConversationAsync(app);
        await using var hub = await HubTestHelper.StartAsync(app, token);

        var ex = await Assert.ThrowsAsync<HubException>(() => hub.InvokeAsync("SendTurn", convId, "   "));
        Assert.Contains("empty message", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendTurn_ClosedConversation_Throws()
    {
        using var app = WithTurnGemini(_factory, new SseQueueHandler());
        var (convId, token) = await SeedOwnedConversationAsync(app);

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
            var conv = await db.Conversations.IgnoreQueryFilters().FirstAsync(c => c.Id == convId);
            conv.Status = ConversationStatus.Closed;
            conv.ClosedAt = DateTime.UtcNow;
            conv.ClosedReason = ConversationClosedReasons.UserEnded;
            await db.SaveChangesAsync();
        }

        await using var hub = await HubTestHelper.StartAsync(app, token);
        var ex = await Assert.ThrowsAsync<HubException>(() => hub.InvokeAsync("SendTurn", convId, "still there?"));
        Assert.Contains("conversation closed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendTurn_ChatDisabled_Throws()
    {
        using var app = WithTurnGemini(_factory, new SseQueueHandler());
        var (convId, token) = await SeedOwnedConversationAsync(app, b =>
        {
            b.EnableChat = false;
            b.EnableVoice = true;
        });

        await using var hub = await HubTestHelper.StartAsync(app, token);
        var ex = await Assert.ThrowsAsync<HubException>(() => hub.InvokeAsync("SendTurn", convId, "hola"));
        Assert.Contains("chat disabled", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendTurn_UnauthorizedConnection_FailsStart()
    {
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await using var hub = await HubTestHelper.StartAsync(_factory, Guid.NewGuid().ToString());
        });
    }

    [Fact]
    public async Task SendTurn_ProviderError_EmitsTurnFailed()
    {
        var handler = new GeminiHttpErrorHandler(
            HttpStatusCode.TooManyRequests,
            """{"error":{"status":"RESOURCE_EXHAUSTED","message":"Quota exceeded"}}""");

        using var app = WithTurnGemini(_factory, handler, fallbackMessage: "FALLBACK_HUB");
        var (convId, token) = await SeedOwnedConversationAsync(app);
        await using var hub = await HubTestHelper.StartAsync(app, token);

        var failed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<object>("turnFailed", payload =>
        {
            failed.TrySetResult(ReadString(payload, "errorCode") + "|" + ReadString(payload, "userMessage"));
        });

        await hub.InvokeAsync("SendTurn", convId, "hola");
        var result = await failed.Task.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Contains("gemini_quota_exceeded", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancelTurn_CancelsInFlightTurn()
    {
        // Replace the turn service with one that blocks until the hub cancels the CTS.
        using var app = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IChatTurnService>();
                services.AddSingleton<IChatTurnService, BlockingChatTurnService>();
            });
        });

        var (convId, token) = await SeedOwnedConversationAsync(app);
        await using var hub = await HubTestHelper.StartAsync(app, token);
        // Hub methods are serialized per connection; CancelTurn on the same connection
        // would queue behind the in-flight SendTurn and never run.
        await using var cancelHub = await HubTestHelper.StartAsync(app, token);

        var started = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<object>("turnStarted", payload => started.TrySetResult(ReadGuid(payload, "turnId")));
        hub.On<object>("turnCancelled", payload => cancelled.TrySetResult(ReadGuid(payload, "turnId")));

        await hub.SendAsync("SendTurn", convId, "slow please");
        var turnId = await started.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await cancelHub.InvokeAsync("CancelTurn", turnId);

        var cancelledId = await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(turnId, cancelledId);
    }

    [Fact]
    public async Task PublicTokenOwner_CanSendTurn()
    {
        var handler = new SseQueueHandler();
        handler.EnqueueStream(
            """
            data: {"candidates":[{"content":{"parts":[{"text":"Public ok"}]}}]}

            """);

        using var app = WithTurnGemini(_factory, handler);
        var session = await PublicBotTestHelper.SeedAndStartAsync(app);

        // Ensure EnableChat (already true in helper)
        await using var hub = await HubTestHelper.StartAsync(app, session.AccessToken);
        await hub.InvokeAsync("SubscribeConversation", session.ConversationId);

        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<object>("turnCompleted", _ => completed.TrySetResult());
        await hub.InvokeAsync("SendTurn", session.ConversationId, "hola publico");
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task SubscribeEvents_WithoutTelemetryPermission_Throws()
    {
        using var app = WithTurnGemini(_factory, new SseQueueHandler());
        // public@example.com only has bot:chat
        var client = app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var login = await client.PostAsJsonAsync("/api/auth/dev/token", new
        {
            email = "public@example.com",
            organizationApplicationId = 1,
        });
        login.EnsureSuccessStatusCode();
        var token = await client.GetStringAsync("/api/auth/hub-token");

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
            var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
            tenant.SetTenant(DbSeeder.SuperAdminOrgId);
            var bot = new Bot
            {
                BotName = "Events Bot",
                UrlSlug = $"ev-{Guid.NewGuid():N}",
                PublicId = PublicIdGenerator.NewId(),
                PublishedState = BotStates.Published,
                EnableChat = true,
                EnableVoice = false,
                MasterPrompt = "x",
            };
            db.Bots.Add(bot);
            await db.SaveChangesAsync();
            db.Conversations.Add(new Conversation
            {
                BotId = bot.Id,
                UserId = "00000000-0000-0000-0000-000000000007",
                Status = ConversationStatus.Active,
            });
            await db.SaveChangesAsync();
            var convId = await db.Conversations.IgnoreQueryFilters()
                .Where(c => c.UserId == "00000000-0000-0000-0000-000000000007")
                .OrderByDescending(c => c.Id)
                .Select(c => c.Id)
                .FirstAsync();

            await using var hub = await HubTestHelper.StartAsync(app, token);
            var ex = await Assert.ThrowsAsync<HubException>(() => hub.InvokeAsync("SubscribeEvents", convId));
            Assert.Contains("telemetry:read", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class BlockingChatTurnService : IChatTurnService
    {
        public async Task<ChatTurnResult> ExecuteAsync(
            ChatTurnExecuteRequest request,
            Func<string, CancellationToken, Task> onDelta,
            CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return new ChatTurnResult("", "test", null, 0, 0, [], null, false);
        }
    }

    private static Guid ReadGuid(object payload, string name)
    {
        var el = System.Text.Json.JsonSerializer.SerializeToElement(payload);
        return el.GetProperty(name).GetGuid();
    }

    private static string ReadString(object payload, string name)
    {
        var el = System.Text.Json.JsonSerializer.SerializeToElement(payload);
        return el.GetProperty(name).GetString() ?? "";
    }
}
