namespace BotFactory.Core.Entities;

/// <summary>
/// Server-side auth session. Stores GOB-ID/Fake tokens so they are never exposed to the browser.
/// The browser only receives an opaque session ID cookie.
/// NOTE: Session storage is temporarily in the database; could move to Redis for better performance.
/// </summary>
public class Session
{
    public Guid Id { get; set; }
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public string AuthProvider { get; set; } = string.Empty; // "gobid" or "fake"
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
