using BotFactory.Core.Services;

namespace BotFactory.Tests;

public class ProviderErrorClassifierTests
{
    [Fact]
    public void ClassifyGemini_429_ReturnsQuotaExceeded()
    {
        var body = """{"error":{"code":429,"message":"Quota exceeded","status":"RESOURCE_EXHAUSTED"}}""";
        var info = ProviderErrorClassifier.ClassifyGemini(429, body);

        Assert.Equal(ProviderErrorClassifier.CodeGeminiQuotaExceeded, info.ErrorCode);
        Assert.Equal("gemini", info.Provider);
        Assert.Equal("llm", info.Stage);
        Assert.Contains("cuota", info.OperatorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ProviderErrorClassifier.UserMessageUnavailable, info.UserMessage);
    }

    [Fact]
    public void ClassifyGemini_403_ReturnsAuthFailed()
    {
        var info = ProviderErrorClassifier.ClassifyGemini(403, "API key not valid");

        Assert.Equal(ProviderErrorClassifier.CodeGeminiAuthFailed, info.ErrorCode);
        Assert.Contains("clave API", info.OperatorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClassifyAgentError_ElevenLabsQuota_ReturnsQuotaExceeded()
    {
        var info = ProviderErrorClassifier.ClassifyAgentError(
            "tts-elevenlabs",
            "You have insufficient credits",
            402);

        Assert.Equal(ProviderErrorClassifier.CodeElevenLabsQuotaExceeded, info.ErrorCode);
        Assert.Equal("elevenlabs", info.Provider);
        Assert.Equal("tts", info.Stage);
        Assert.Contains("ElevenLabs", info.OperatorMessage);
    }

    [Fact]
    public void ClassifyAgentError_SttElevenLabs_ReturnsSttStage()
    {
        var info = ProviderErrorClassifier.ClassifyAgentError(
            "stt-elevenlabs",
            "quota exceeded");

        Assert.Equal("stt", info.Stage);
        Assert.Equal(ProviderErrorClassifier.CodeElevenLabsQuotaExceeded, info.ErrorCode);
    }
}
