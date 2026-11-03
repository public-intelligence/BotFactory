using System.Text.Json;
using BotFactory.Core.Auth;
using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using BotFactory.Infrastructure.Data;
using BotFactory.Infrastructure.Tenancy;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BotFactory.Api.Hubs;

public class ConversationHub : Hub
{
    private readonly BotFactoryDbContext _db;
    private readonly TenantContext _tenantContext;
    private readonly IChatTurnService _chatTurnService;
    private readonly IChatTurnRegistry _turnRegistry;

    public ConversationHub(
        BotFactoryDbContext db,
        TenantContext tenantContext,
        IChatTurnService chatTurnService,
        IChatTurnRegistry turnRegistry)
    {
        _db = db;
        _tenantContext = tenantContext;
        _chatTurnService = chatTurnService;
        _turnRegistry = turnRegistry;
    }

    public static string MessagesGroup(int conversationId) => $"conv:{conversationId}";
    public static string EventsGroup(int conversationId) => $"conv:{conversationId}:events";

    public async Task SubscribeConversation(int conversationId)
    {
        HydrateTenant();
        var conversation = await LoadAuthorizedConversation(conversationId);
        await Groups.AddToGroupAsync(Context.ConnectionId, MessagesGroup(conversationId));
        _ = conversation;
    }

    public Task UnsubscribeConversation(int conversationId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, MessagesGroup(conversationId));

    public async Task SubscribeEvents(int conversationId)
    {
        HydrateTenant();
        if (!_tenantContext.Has(Permissions.TelemetryRead) && !_tenantContext.Has(Permissions.SuperAdmin) && !_tenantContext.Has(Permissions.BotAdmin))
            throw new HubException("forbidden: telemetry:read required");

        _ = await LoadAuthorizedConversation(conversationId);
        await Groups.AddToGroupAsync(Context.ConnectionId, EventsGroup(conversationId));
    }

    public Task UnsubscribeEvents(int conversationId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, EventsGroup(conversationId));

    public async Task SendTurn(int conversationId, string text)
    {
        HydrateTenant();

        if (string.IsNullOrWhiteSpace(text))
            throw new HubException("empty message");

        var conversation = await LoadAuthorizedConversation(conversationId);
        if (conversation.Status == ConversationStatus.Closed)
            throw new HubException("conversation closed");

        var bot = await _db.Bots.AsNoTracking().FirstOrDefaultAsync(b => b.Id == conversation.BotId);
        if (bot is null)
            throw new HubException("bot not found");

        if (!bot.EnableChat)
            throw new HubException("chat disabled for this bot");

        var turnId = Guid.NewGuid();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Context.ConnectionAborted);
        if (!_turnRegistry.TryRegister(turnId, Context.ConnectionId, _tenantContext.UserId, cts))
            throw new HubException("turn already active");

        try
        {
            await Clients.Caller.SendAsync("turnStarted", new { turnId, conversationId });

            ChatTurnProviderError? providerError = null;
            try
            {
                var result = await _chatTurnService.ExecuteAsync(
                    new ChatTurnExecuteRequest(conversationId, text.Trim(), turnId),
                    async (delta, ct) =>
                    {
                        await Clients.Caller.SendAsync("turnDelta", new { turnId, text = delta }, ct);
                    },
                    cts.Token);

                providerError = result.ProviderError;
                await Clients.Caller.SendAsync("turnCompleted", new { turnId });
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                await Clients.Caller.SendAsync("turnCancelled", new { turnId });
                return;
            }

            if (providerError is not null)
            {
                await Clients.Caller.SendAsync("turnFailed", new
                {
                    turnId,
                    userMessage = providerError.UserMessage,
                    errorCode = providerError.ErrorCode,
                });
            }
        }
        finally
        {
            _turnRegistry.Unregister(turnId);
        }
    }

    public Task CancelTurn(Guid turnId)
    {
        HydrateTenant();
        _turnRegistry.TryCancel(turnId, Context.ConnectionId, _tenantContext.UserId);
        return Task.CompletedTask;
    }

    private void HydrateTenant()
    {
        var user = Context.User;
        if (user is null)
            throw new HubException("unauthenticated");

        _tenantContext.OrganizationId = user.FindFirst("organization_id")?.Value ?? string.Empty;
        _tenantContext.UserId = user.FindFirst("sub")?.Value ?? string.Empty;
        _tenantContext.FullName = user.FindFirst("person_full_name")?.Value ?? string.Empty;
        _tenantContext.OrganizationName = user.FindFirst("organization_name")?.Value ?? string.Empty;

        var permClaim = user.FindFirst("permissions")?.Value;
        if (!string.IsNullOrEmpty(permClaim))
        {
            try
            {
                _tenantContext.Permissions = JsonSerializer.Deserialize<List<string>>(permClaim) ?? [];
            }
            catch (JsonException)
            {
                _tenantContext.Permissions = [];
            }
        }
        else
        {
            _tenantContext.Permissions = [];
        }
    }

    private async Task<Conversation> LoadAuthorizedConversation(int conversationId)
    {
        var conv = await _db.Conversations
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == conversationId);

        if (conv is null)
            throw new HubException($"conversation {conversationId} not found");

        if (!ConversationAuthorization.CanView(conv, _tenantContext))
            throw new HubException("forbidden");

        return conv;
    }
}
