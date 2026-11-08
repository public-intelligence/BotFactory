using BotFactory.Core.Entities;
using BotFactory.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace BotFactory.Infrastructure.Services;

/// <summary>
/// The voice room of a conversation: at most one at a time, and it ends when the call does.
/// Every voice call is a LiveKit room with an agent in it, and every agent holds a speech-recognition
/// slot of the provider (nine at once, shared by everyone). Nothing tells LiveKit that a call is over:
/// left alone, a room outlives its call by 20 s after the person leaves and by 5 minutes when nobody
/// joined. This keeps the conversation's current room in <see cref="Conversation.VoiceRoomName"/> and
/// retires it when the call ends (the person goes back to text, the conversation is closed) or is
/// replaced (a new call for the same conversation), so the agent leaves at once and the calls never pile up.
/// </summary>
public interface IVoiceRoomLifecycle
{
    /// <summary>Opens the room for a new voice call of the conversation, retiring the one it had, and records it.</summary>
    Task<LiveKitRoomResult> OpenAsync(
        Conversation conversation,
        RoomMetadata metadata,
        AgentJobMetadata jobMetadata,
        string agentName,
        string roomName,
        TimeSpan? tokenTtl = null,
        bool canPublishData = true,
        CancellationToken cancellationToken = default);

    /// <summary>Retires the conversation's voice room, if it has one. Never throws: the room times out on its own.</summary>
    Task RetireAsync(Conversation conversation, CancellationToken cancellationToken = default);
}

public class VoiceRoomLifecycle(
    ILiveKitRooms rooms,
    BotFactoryDbContext db,
    ILogger<VoiceRoomLifecycle> logger) : IVoiceRoomLifecycle
{
    public async Task<LiveKitRoomResult> OpenAsync(
        Conversation conversation,
        RoomMetadata metadata,
        AgentJobMetadata jobMetadata,
        string agentName,
        string roomName,
        TimeSpan? tokenTtl = null,
        bool canPublishData = true,
        CancellationToken cancellationToken = default)
    {
        // The previous call goes first: if the person is still in it (a reconnect), the new one replaces it.
        await RetireAsync(conversation, cancellationToken);

        var result = await rooms.CreateRoomAsync(metadata, jobMetadata, agentName, roomName, tokenTtl, canPublishData);

        conversation.VoiceRoomName = result.RoomName;
        await db.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task RetireAsync(Conversation conversation, CancellationToken cancellationToken = default)
    {
        var roomName = conversation.VoiceRoomName;
        if (string.IsNullOrEmpty(roomName))
            return;

        // The name is forgotten first: whatever happens to the room, it is no longer the conversation's.
        conversation.VoiceRoomName = null;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not forget voice room {RoomName} of conversation {ConversationId}", roomName, conversation.Id);
        }

        try
        {
            await rooms.DeleteRoomAsync(roomName);
            logger.LogInformation(
                "Voice room retired conversation={ConversationId} room={RoomName}", conversation.Id, roomName);
        }
        catch (Exception ex)
        {
            // Best effort: the room ends by itself (empty or departure timeout) a minute or so later.
            logger.LogWarning(
                ex, "Could not delete voice room {RoomName} of conversation {ConversationId}; it will time out",
                roomName, conversation.Id);
        }
    }
}
