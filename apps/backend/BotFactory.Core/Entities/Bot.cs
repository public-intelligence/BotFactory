using BotFactory.Core.DTOs;

namespace BotFactory.Core.Entities;

public class Bot : BaseEntity, IAuditable
{
    public string BotName { get; set; } = string.Empty;
    public string UrlSlug { get; set; } = string.Empty;
    public string PublishedState { get; set; } = BotStates.Draft;
    public string MasterPrompt { get; set; } = string.Empty;
    public int TopK { get; set; } = 5;
    public int SilenceEndMs { get; set; } = 700;
    public int NoSpeechTimeoutMs { get; set; } = 15000;
    public int MaxUtteranceMs { get; set; } = 20000;
    public bool EnableBargeIn { get; set; } = true;
    public bool EnableTts { get; set; } = true;
    public bool EnableTypingSound { get; set; } = true;
    public bool EnableChat { get; set; } = false;
    public bool EnableVoice { get; set; } = true;

    // Optional per-bot MP3 played in the browser while the agent is "thinking".
    // When null, the chat UI uses the bundled default thinking sound.
    public string? ThinkingSoundBlobKey { get; set; }
    public string? ThinkingSoundFilename { get; set; }

    // Optional per-bot branding (Apariencia). Null colors = platform dark defaults.
    // Hex strings normalized to #RRGGBB. Logo served via short-lived SAS like thinking sound.
    public string? ThemeBackground { get; set; }
    public string? ThemePrimary { get; set; }
    public string? ThemeAccent { get; set; }
    public string? LogoBlobKey { get; set; }
    public string? LogoFilename { get; set; }

    public string? GroundedOnlyMessage { get; set; }
    public double SimilarityThreshold { get; set; } = 0.4;

    /// <summary>
    /// «Respaldo obligatorio»: every answer must come from the bot's sources (knowledge
    /// base, tools, prompt). The first Gemini call of each turn must consult a tool, recent
    /// tool results ride in the history, and a sentence carrying a datum (phone, link,
    /// e-mail, amount, number) that no source contains is not delivered — the bot says
    /// it has no confirmed information (<see cref="GroundedOnlyMessage"/>) instead.
    /// </summary>
    public bool RequireGrounding { get; set; }
    public string? VoiceId { get; set; }

    // Spoken by the agent immediately after session start, before the user speaks.
    // Null/empty disables the greeting (bot waits silently). Sent to the agent via
    // RoomMetadata and played with session.say() in apps/agent/.../agent.py.
    public string? GreetingMessage { get; set; } = "¡Hola! Estoy para servirte.";

    // Silero VAD (Voice Activity Detection) tuning. Sent to the agent via RoomMetadata
    // and used in apps/agent/src/bot_factory_agent/agent.py:get_vad_for_bot to construct
    // a per-bot silero.VAD instance. Higher activation_threshold = less sensitive to
    // background noise; longer min_speech_duration ignores short blips (claps, coughs).
    // SilenceEndMs maps to Silero min_silence_duration (VAD floor). End-of-turn also uses
    // LiveKit's multilingual turn detector in apps/agent (semantic completion, not silence-only).
    public double VadActivationThreshold { get; set; } = 0.80;
    public int VadMinSpeechDurationMs { get; set; } = 400;

    // Interruption (barge-in) gating. Sent to the agent via RoomMetadata and used in
    // apps/agent/src/bot_factory_agent/agent.py turn_handling. Higher values make the
    // bot harder to interrupt: a stray word or short noise won't cut it off.
    public int InterruptionMinDurationMs { get; set; } = 1500;
    public int InterruptionMinWords { get; set; } = 1;

    // Public access & embedding (planning/anonymous-access-embedding-plan.md).
    // When false, anyone with the public link can chat without GOB-ID login —
    // but only while the bot is published. Admin/config surfaces stay protected.
    public bool RequiresAuthentication { get; set; } = true;

    // Opaque, globally-unique key for the public/embed URL. UrlSlug is only
    // unique per organization, so it can't safely address a bot anonymously
    // across tenants. Generated server-side on create; never client-supplied.
    public string PublicId { get; set; } = string.Empty;

    // Embedding is opt-in and independent from public-link access.
    public bool EmbedEnabled { get; set; }

    // Comma/newline-separated list of parent origins allowed to iframe this bot
    // (e.g. "https://ministerio.gob.do"). Empty means: no external site may
    // embed (only the platform's own origin, for the admin preview).
    public string AllowedEmbedOrigins { get; set; } = string.Empty;

    // Per-bot cap on anonymous session-start/resume requests per minute.
    // Null = the platform default (PublicAccessOptions.BotRequestsPerMinute).
    public int? PublicRequestsPerMinute { get; set; }

    public void ApplyUpdate(UpdateBotRequest request)
    {
        if (request.BotName is not null) BotName = request.BotName;

        if (request.UrlSlug is not null) UrlSlug = request.UrlSlug;
        if (request.PublishedState is not null) PublishedState = request.PublishedState;
        if (request.MasterPrompt is not null) MasterPrompt = request.MasterPrompt;
        if (request.TopK is not null) TopK = request.TopK.Value;
        if (request.SilenceEndMs is not null) SilenceEndMs = request.SilenceEndMs.Value;
        if (request.NoSpeechTimeoutMs is not null) NoSpeechTimeoutMs = request.NoSpeechTimeoutMs.Value;
        if (request.MaxUtteranceMs is not null) MaxUtteranceMs = request.MaxUtteranceMs.Value;
        if (request.EnableBargeIn is not null) EnableBargeIn = request.EnableBargeIn.Value;
        if (request.EnableTts is not null) EnableTts = request.EnableTts.Value;
        if (request.EnableTypingSound is not null) EnableTypingSound = request.EnableTypingSound.Value;
        if (request.EnableChat is not null) EnableChat = request.EnableChat.Value;
        if (request.EnableVoice is not null) EnableVoice = request.EnableVoice.Value;
        if (request.GroundedOnlyMessage is not null) GroundedOnlyMessage = request.GroundedOnlyMessage;
        if (request.SimilarityThreshold.HasValue) SimilarityThreshold = request.SimilarityThreshold.Value;
        if (request.RequireGrounding is not null) RequireGrounding = request.RequireGrounding.Value;
        if (request.VoiceId is not null) VoiceId = request.VoiceId.Length == 0 ? null : request.VoiceId;
        if (request.GreetingMessage is not null)
            GreetingMessage = string.IsNullOrWhiteSpace(request.GreetingMessage)
                ? null
                : request.GreetingMessage.Trim();
        if (request.VadActivationThreshold.HasValue) VadActivationThreshold = request.VadActivationThreshold.Value;
        if (request.VadMinSpeechDurationMs is not null) VadMinSpeechDurationMs = request.VadMinSpeechDurationMs.Value;
        if (request.InterruptionMinDurationMs is not null) InterruptionMinDurationMs = request.InterruptionMinDurationMs.Value;
        if (request.InterruptionMinWords is not null) InterruptionMinWords = request.InterruptionMinWords.Value;
        if (request.RequiresAuthentication is not null) RequiresAuthentication = request.RequiresAuthentication.Value;
        if (request.EmbedEnabled is not null) EmbedEnabled = request.EmbedEnabled.Value;
        if (request.AllowedEmbedOrigins is not null) AllowedEmbedOrigins = EmbedOrigins.Normalize(request.AllowedEmbedOrigins);
        // 0 (or negative) is the "clear override" sentinel: null in the PATCH
        // body means "no change" in this DTO convention, so it can't mean
        // "back to platform default" as well.
        if (request.PublicRequestsPerMinute is not null)
            PublicRequestsPerMinute = request.PublicRequestsPerMinute.Value <= 0 ? null : request.PublicRequestsPerMinute.Value;
        // PublicId is intentionally not updatable from the API: it's a stable,
        // server-generated key that distributed links and embeds depend on.

        // Empty string clears a theme color back to the platform default (null).
        // Null in the PATCH body means "no change".
        if (request.ThemeBackground is not null)
            ThemeBackground = ThemeColors.NormalizeOrNull(request.ThemeBackground);
        if (request.ThemePrimary is not null)
            ThemePrimary = ThemeColors.NormalizeOrNull(request.ThemePrimary);
        if (request.ThemeAccent is not null)
            ThemeAccent = ThemeColors.NormalizeOrNull(request.ThemeAccent);
    }
}
