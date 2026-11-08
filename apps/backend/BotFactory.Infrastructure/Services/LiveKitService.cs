using System.Text.Json;
using BotFactory.Core.Services;
using Livekit.Server.Sdk.Dotnet;
using Microsoft.Extensions.Options;

namespace BotFactory.Infrastructure.Services;

public record LiveKitRoomResult(string RoomName, string Token, string Url);

/// <summary>
/// Mirrors the Python agent's RoomMetadata dataclass (types.py).
/// Serialized as JSON into LiveKit room.metadata, which LiveKit delivers to every
/// participant, anonymous visitors included. Never add secrets here: they belong
/// in <see cref="AgentJobMetadata"/>.
/// </summary>
public record RoomMetadata(
    int BotId,
    int ConversationId,
    string VoiceId,
    int NoSpeechTimeoutMs,
    int MaxUtteranceMs,
    bool EnableBargeIn,
    int SilenceEndMs,
    double VadActivationThreshold,
    int VadMinSpeechDurationMs,
    int InterruptionMinDurationMs,
    int InterruptionMinWords,
    string? GreetingMessage = null,
    bool IsResume = false,
    // Words the speech recogniser should favour for this bot; the agent hands them to the
    // provider when it opens the transcription stream. Not a secret: room metadata reaches
    // every participant.
    IReadOnlyList<string>? TranscriptionVocabulary = null);

/// <summary>
/// Secrets for the agent, serialized as JSON into the agent dispatch metadata.
/// LiveKit hands it to the dispatched worker as job.metadata; participants never receive it.
/// Parsed by config.py in the Python agent.
/// </summary>
public record AgentJobMetadata(string ElevenLabsApiKey);

/// <summary>
/// What the API needs from LiveKit to run a voice call: open a room (with its agent) and delete it.
/// An interface so tests can stand in for the server.
/// </summary>
public interface ILiveKitRooms
{
    Task<LiveKitRoomResult> CreateRoomAsync(
        RoomMetadata metadata,
        AgentJobMetadata jobMetadata,
        string agentName,
        string? roomName = null,
        TimeSpan? tokenTtl = null,
        bool canPublishData = true);

    /// <summary>Deletes the room, which disconnects whoever is in it, the agent included. Throws if LiveKit fails.</summary>
    Task DeleteRoomAsync(string roomName);
}

public class LiveKitService : ILiveKitRooms
{
    private static readonly JsonSerializerOptions CamelCase = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly LiveKitOptions _options;
    private readonly RoomServiceClient _roomClient;

    public LiveKitService(IOptions<LiveKitOptions> options)
    {
        _options = options.Value;
        _roomClient = new RoomServiceClient(_options.Host, _options.ApiKey, _options.ApiSecret);
    }

    public async Task<LiveKitRoomResult> CreateRoomAsync(
        RoomMetadata metadata,
        AgentJobMetadata jobMetadata,
        string agentName,
        string? roomName = null,
        TimeSpan? tokenTtl = null,
        bool canPublishData = true)
    {
        roomName ??= metadata.ConversationId.ToString();

        await _roomClient.CreateRoom(BuildCreateRoomRequest(
            metadata, jobMetadata, agentName, roomName,
            _options.RoomEmptyTimeoutSeconds, _options.RoomDepartureTimeoutSeconds));

        var token = GenerateToken(
            roomName,
            $"user-{metadata.BotId}-{metadata.ConversationId}",
            "Web User",
            tokenTtl,
            canPublishData);

        var publicHost = string.IsNullOrEmpty(_options.PublicHost) ? _options.Host : _options.PublicHost;
        var wsUrl = publicHost
            .Replace("https://", "wss://")
            .Replace("http://", "ws://");

        return new LiveKitRoomResult(roomName, token, wsUrl);
    }

    public async Task DeleteRoomAsync(string roomName)
        => await _roomClient.DeleteRoom(new DeleteRoomRequest { Room = roomName });

    public static CreateRoomRequest BuildCreateRoomRequest(
        RoomMetadata metadata,
        AgentJobMetadata jobMetadata,
        string agentName,
        string roomName,
        int emptyTimeoutSeconds = 60,
        int departureTimeoutSeconds = 20)
    {
        var request = new CreateRoomRequest
        {
            Name = roomName,
            Metadata = JsonSerializer.Serialize(metadata, CamelCase),
            // A room nobody joins, and a room everybody left, close on their own; the agent in them goes with them.
            EmptyTimeout = (uint)Math.Max(1, emptyTimeoutSeconds),
            DepartureTimeout = (uint)Math.Max(1, departureTimeoutSeconds),
        };

        request.Agents.Add(new RoomAgentDispatch
        {
            AgentName = agentName,
            Metadata = JsonSerializer.Serialize(jobMetadata, CamelCase),
        });

        return request;
    }

    // Anonymous sessions get a shorter TTL and no data-channel publish grant
    // (the browser only listens on the "agent" topic; text rides SignalR).
    public string GenerateToken(
        string roomName,
        string participantIdentity,
        string participantName,
        TimeSpan? ttl = null,
        bool canPublishData = true)
    {
        var token = new AccessToken(_options.ApiKey, _options.ApiSecret)
            .WithIdentity(participantIdentity)
            .WithName(participantName)
            .WithGrants(new VideoGrants
            {
                RoomJoin = true,
                Room = roomName,
                CanPublish = true,
                CanSubscribe = true,
                CanPublishData = canPublishData,
            })
            .WithTtl(ttl ?? TimeSpan.FromHours(1));

        return token.ToJwt();
    }
}
