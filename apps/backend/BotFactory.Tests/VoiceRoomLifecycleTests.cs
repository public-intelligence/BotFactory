using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using BotFactory.Infrastructure.Data;
using BotFactory.Infrastructure.Services;
using BotFactory.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BotFactory.Tests;

/// <summary>
/// A voice call is a LiveKit room with an agent in it, and every agent holds one of the nine
/// speech-recognition slots. The room of a call ends when the call does: the person goes back to text,
/// the conversation is closed, or a new call replaces it. A conversation never has two rooms at once, and
/// an agent that is leaving cannot pause or close the conversation it no longer belongs to.
/// </summary>
[Collection("LiveKitTurn")]
public class VoiceRoomLifecycleTests
{
    private readonly LiveKitWebApplicationFactory _factory;

    public VoiceRoomLifecycleTests(LiveKitWebApplicationFactory factory) => _factory = factory;

    private sealed class FakeRooms : ILiveKitRooms
    {
        public List<string> Created { get; } = [];
        public List<string> Deleted { get; } = [];
        public HashSet<string> Alive { get; } = [];
        public bool FailDelete { get; set; }

        public Task<LiveKitRoomResult> CreateRoomAsync(
            RoomMetadata metadata, AgentJobMetadata jobMetadata, string agentName,
            string? roomName = null, TimeSpan? tokenTtl = null, bool canPublishData = true)
        {
            Created.Add(roomName!);
            Alive.Add(roomName!);
            return Task.FromResult(new LiveKitRoomResult(roomName!, "room-token", "wss://livekit.test"));
        }

        public Task DeleteRoomAsync(string roomName)
        {
            if (FailDelete)
                throw new InvalidOperationException("LiveKit is down");
            Deleted.Add(roomName);
            Alive.Remove(roomName);
            return Task.CompletedTask;
        }
    }

    private sealed class KeyedSettings : IAppSettingService
    {
        public Task<string?> GetDecryptedKeyAsync(ServiceType serviceType) => Task.FromResult<string?>("fake-key");
        public Task<List<AppSetting>> GetAllAsync() => Task.FromResult(new List<AppSetting>());
        public Task<AppSetting?> GetByServiceTypeAsync(ServiceType serviceType) => Task.FromResult<AppSetting?>(null);
        public Task<List<AppSetting>> BulkUpsertAsync(List<(ServiceType ServiceType, string ApiKey)> settings) =>
            Task.FromResult(new List<AppSetting>());
        public Task<bool> DeleteAsync(ServiceType serviceType) => Task.FromResult(false);
    }

    private (WebApplicationFactory<Program> App, FakeRooms Rooms) NewApp()
    {
        var rooms = new FakeRooms();
        var app = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ILiveKitRooms>();
            services.AddSingleton<ILiveKitRooms>(rooms);
            services.RemoveAll<IAppSettingService>();
            services.AddSingleton<IAppSettingService>(new KeyedSettings());
        }));
        return (app, rooms);
    }

    private static async Task<PublicBotSession> StartAsync(WebApplicationFactory<Program> app, string mode)
    {
        var bot = await PublicBotTestHelper.SeedPublishedPublicBotAsync(app, b =>
        {
            b.EnableChat = true;
            b.EnableVoice = true;
        });
        return await PublicBotTestHelper.StartPublicChatAsync(app, bot, mode);
    }

    private static async Task<HttpResponseMessage> ResumeAsync(
        WebApplicationFactory<Program> app, PublicBotSession session, string mode)
    {
        using var client = PublicBotTestHelper.CreatePublicClient(app, session.AccessToken);
        return await client.PostAsync(
            $"/api/public/bots/{session.Bot.PublicId}/conversations/{session.ConversationId}/resume?mode={mode}", null);
    }

    private static async Task<HttpResponseMessage> CloseAsync(
        WebApplicationFactory<Program> app, PublicBotSession session)
    {
        using var client = PublicBotTestHelper.CreatePublicClient(app, session.AccessToken);
        return await client.PostAsJsonAsync(
            $"/api/public/bots/{session.Bot.PublicId}/conversations/{session.ConversationId}/close",
            new { reason = "user_ended" });
    }

    private static async Task<Conversation> ConversationAsync(WebApplicationFactory<Program> app, int id)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotFactoryDbContext>();
        return await db.Conversations.IgnoreQueryFilters().AsNoTracking().FirstAsync(c => c.Id == id);
    }

    /// <summary>What the agent posts to Bot Factory about its session.</summary>
    private static async Task PostAgentEventAsync(
        WebApplicationFactory<Program> app, PublicBotSession session, string eventType, object? data, string? roomName)
    {
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("X-Agent-Secret", "dev-agent-callback-secret");
        var body = JsonSerializer.Serialize(new
        {
            eventType,
            botId = session.Bot.Id,
            conversationId = session.ConversationId,
            timestamp = DateTime.UtcNow.ToString("O"),
            data,
            roomName,
        });
        var response = await client.PostAsync(
            "/livekit/telemetry/events", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // ---- The room of a call is recorded ----

    [Fact]
    public async Task StartingAVoiceCall_RecordsItsRoom()
    {
        var (app, rooms) = NewApp();
        using var _ = app;

        var session = await StartAsync(app, "voice");

        var room = Assert.Single(rooms.Created);
        var conversation = await ConversationAsync(app, session.ConversationId);
        Assert.Equal(room, conversation.VoiceRoomName);
        Assert.Equal(ConversationChannels.Voice, conversation.Channel);
        Assert.Contains(room, rooms.Alive);
    }

    [Fact]
    public async Task StartingATextConversation_HasNoRoom_AndNeverTouchesLiveKit()
    {
        var (app, rooms) = NewApp();
        using var _ = app;

        var session = await StartAsync(app, "text");
        Assert.Equal(HttpStatusCode.NoContent, (await CloseAsync(app, session)).StatusCode);

        Assert.Empty(rooms.Created);
        Assert.Empty(rooms.Deleted);
        Assert.Null((await ConversationAsync(app, session.ConversationId)).VoiceRoomName);
    }

    // ---- A conversation never has two rooms ----

    [Fact]
    public async Task ResumingInVoice_RetiresThePreviousRoom_SoOnlyOneStaysOpen()
    {
        var (app, rooms) = NewApp();
        using var _ = app;
        var session = await StartAsync(app, "voice");
        var first = Assert.Single(rooms.Created);

        var resumed = await ResumeAsync(app, session, "voice");

        Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
        Assert.Equal(2, rooms.Created.Count);
        var second = rooms.Created[1];
        Assert.NotEqual(first, second);
        Assert.Equal([first], rooms.Deleted);
        Assert.Equal([second], rooms.Alive.ToList());
        Assert.Equal(second, (await ConversationAsync(app, session.ConversationId)).VoiceRoomName);
    }

    // ---- The room goes when the call does ----

    [Fact]
    public async Task ResumingInText_RetiresTheRoom_AndMovesTheConversationToText_WithoutClosingIt()
    {
        var (app, rooms) = NewApp();
        using var _ = app;
        var session = await StartAsync(app, "voice");
        var room = Assert.Single(rooms.Created);

        var resumed = await ResumeAsync(app, session, "text");

        Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
        Assert.Equal([room], rooms.Deleted);
        Assert.Empty(rooms.Alive);
        var conversation = await ConversationAsync(app, session.ConversationId);
        Assert.Null(conversation.VoiceRoomName);
        Assert.Equal(ConversationChannels.Text, conversation.Channel);
        Assert.Equal(ConversationStatus.Active, conversation.Status);
    }

    [Fact]
    public async Task ClosingTheConversation_RetiresItsRoom()
    {
        var (app, rooms) = NewApp();
        using var _ = app;
        var session = await StartAsync(app, "voice");
        var room = Assert.Single(rooms.Created);

        Assert.Equal(HttpStatusCode.NoContent, (await CloseAsync(app, session)).StatusCode);

        Assert.Equal([room], rooms.Deleted);
        var conversation = await ConversationAsync(app, session.ConversationId);
        Assert.Equal(ConversationStatus.Closed, conversation.Status);
        Assert.Null(conversation.VoiceRoomName);
    }

    [Fact]
    public async Task WhenLiveKitFailsToDeleteTheRoom_ResumeAndCloseStillWork_AndTheRoomIsForgotten()
    {
        var (app, rooms) = NewApp();
        using var _ = app;
        rooms.FailDelete = true;

        var toText = await StartAsync(app, "voice");
        Assert.Equal(HttpStatusCode.OK, (await ResumeAsync(app, toText, "text")).StatusCode);
        Assert.Null((await ConversationAsync(app, toText.ConversationId)).VoiceRoomName);

        var toClose = await StartAsync(app, "voice");
        Assert.Equal(HttpStatusCode.NoContent, (await CloseAsync(app, toClose)).StatusCode);
        var closed = await ConversationAsync(app, toClose.ConversationId);
        Assert.Equal(ConversationStatus.Closed, closed.Status);
        Assert.Null(closed.VoiceRoomName);
        // The rooms are still up: LiveKit will time them out; the API did what it could.
        Assert.Equal(2, rooms.Alive.Count);
    }

    // ---- An agent that is leaving cannot touch the conversation it no longer belongs to ----

    [Theory]
    [InlineData("Close", "job_shutdown")]
    [InlineData("Close", "user_initiated")]
    [InlineData("SessionPaused", "no_speech_timeout")]
    public async Task AnAgentEvent_NeverClosesOrPausesAConversationOnTheTextChannel(string eventType, string reason)
    {
        var (app, rooms) = NewApp();
        using var _ = app;
        var session = await StartAsync(app, "voice");
        var room = Assert.Single(rooms.Created);
        await ResumeAsync(app, session, "text");

        // The agent of the retired call says goodbye after the person went back to the chat.
        await PostAgentEventAsync(app, session, eventType, new { reason }, room);

        Assert.Equal(ConversationStatus.Active, (await ConversationAsync(app, session.ConversationId)).Status);
    }

    [Fact]
    public async Task AnAgentEvent_FromARoomThatReplacedByANewCall_IsIgnored_ButTheCurrentOneIsNot()
    {
        var (app, rooms) = NewApp();
        using var _ = app;
        var session = await StartAsync(app, "voice");
        var first = Assert.Single(rooms.Created);
        await ResumeAsync(app, session, "voice");
        var second = rooms.Created[1];

        // The first call's agent is leaving: its «shutdown» must not pause the second call.
        await PostAgentEventAsync(app, session, "Close", new { reason = "job_shutdown" }, first);
        Assert.Equal(ConversationStatus.Active, (await ConversationAsync(app, session.ConversationId)).Status);

        // The current call's agent ending for lack of speech does pause the conversation, as always.
        await PostAgentEventAsync(app, session, "SessionPaused", new { reason = "no_speech_timeout" }, second);
        Assert.Equal(ConversationStatus.Paused, (await ConversationAsync(app, session.ConversationId)).Status);
    }

    [Fact]
    public async Task AnAgentEvent_WithoutARoomName_IsHandledAsBefore()
    {
        var (app, _) = NewApp();
        using var __ = app;
        var session = await StartAsync(app, "voice");

        // Agents that predate the room name still pause the conversation of a voice call.
        await PostAgentEventAsync(app, session, "SessionPaused", new { reason = "no_speech_timeout" }, roomName: null);

        Assert.Equal(ConversationStatus.Paused, (await ConversationAsync(app, session.ConversationId)).Status);
    }
}
