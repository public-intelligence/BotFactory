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
}
