using BotFactory.Core.Entities;
using BotFactory.Infrastructure.Services;

namespace BotFactory.Api.Helpers;

public static class VoiceChatRoomHelper
{
    public const string DefaultElevenLabsVoiceId = "EXAVITQu4vr4xnSDxMaL";
    public const string AgentName = "bot-factory-agent";

    public static RoomMetadata BuildRoomMetadata(Bot bot, int conversationId, bool isResume = false) =>
        new(
            BotId: bot.Id,
            ConversationId: conversationId,
            VoiceId: bot.VoiceId ?? DefaultElevenLabsVoiceId,
            NoSpeechTimeoutMs: bot.NoSpeechTimeoutMs,
            MaxUtteranceMs: bot.MaxUtteranceMs,
            EnableBargeIn: bot.EnableBargeIn,
            SilenceEndMs: bot.SilenceEndMs,
            VadActivationThreshold: bot.VadActivationThreshold,
            VadMinSpeechDurationMs: bot.VadMinSpeechDurationMs,
            InterruptionMinDurationMs: bot.InterruptionMinDurationMs,
            InterruptionMinWords: bot.InterruptionMinWords,
            GreetingMessage: bot.GreetingMessage,
            IsResume: isResume,
            TranscriptionVocabulary: TranscriptionTerms.Parse(bot.TranscriptionVocabulary));

    public static string NewRoomName(int conversationId, bool isResume) =>
        isResume
            ? $"{conversationId}-r{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
            : conversationId.ToString();
}
