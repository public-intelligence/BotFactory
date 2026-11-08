using System.ComponentModel.DataAnnotations;

namespace BotFactory.Core.Services;

public class LiveKitOptions
{
    [Required] public string Host { get; set; } = "";
    [Required] public string ApiKey { get; set; } = "";
    [Required] public string ApiSecret { get; set; } = "";

    /// <summary>
    /// Public-facing LiveKit URL for browser WebSocket connections.
    /// Falls back to Host if not set.
    /// </summary>
    public string PublicHost { get; set; } = "";

    /// <summary>
    /// Seconds LiveKit keeps a room nobody has joined. A call that never connects (the app lost
    /// its network, the page was closed) leaves an agent in the room for this long, using one of
    /// the speech-recognition slots, so it is short: joining takes seconds. LiveKit's own default is 300.
    /// </summary>
    public int RoomEmptyTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Seconds LiveKit keeps a room after the last person leaves. It is also how long a person who
    /// lost the connection has to come back to the same call, so it stays at LiveKit's default of 20.
    /// </summary>
    public int RoomDepartureTimeoutSeconds { get; set; } = 20;
}
