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
        // The visitor is told what is happening and what to do, not the generic «no disponible».
        Assert.Equal(ProviderErrorClassifier.UserMessageBusy, info.UserMessage);
        Assert.Contains("muchas consultas", info.UserMessage, StringComparison.Ordinal);
    }

    // The body Google sent on staging on 2026-11-04, trimmed to what matters.
    private const string GoogleQuotaBody = """
        {
          "error": {
            "code": 429,
            "message": "You exceeded your current quota, please check your plan and billing details.",
            "status": "RESOURCE_EXHAUSTED",
            "details": [
              { "@type": "type.googleapis.com/google.rpc.Help", "links": [ { "url": "https://ai.google.dev/gemini-api/docs/rate-limits" } ] },
              { "@type": "type.googleapis.com/google.rpc.QuotaFailure", "violations": [ { "quotaValue": "1000000" } ] },
              { "@type": "type.googleapis.com/google.rpc.RetryInfo", "retryDelay": "57s" }
            ]
          }
        }
        """;

    [Fact]
    public void ParseRetryDelay_ReadsTheRetryInfoDetailOfAQuotaError()
    {
        Assert.Equal(TimeSpan.FromSeconds(57), ProviderErrorClassifier.ParseRetryDelay(GoogleQuotaBody));
    }

    [Theory]
    [InlineData("0.5s", 500)]
    [InlineData("2s", 2000)]
    [InlineData("28.9s", 28900)]
    [InlineData("0s", 0)]
    public void ParseRetryDelay_ReadsFractionalAndShortDelays(string delay, double expectedMs)
    {
        var body = "{\"error\":{\"details\":[{\"@type\":\"type.googleapis.com/google.rpc.RetryInfo\",\"retryDelay\":\"" + delay + "\"}]}}";

        Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), ProviderErrorClassifier.ParseRetryDelay(body));
    }

    [Fact]
    public void ParseRetryDelay_AcceptsTheBodyWrappedInAnArray_AsTheStreamingEndpointSendsIt()
    {
        Assert.Equal(TimeSpan.FromSeconds(57), ProviderErrorClassifier.ParseRetryDelay("[" + GoogleQuotaBody + "]"));
    }

    [Fact]
    public void ParseRetryDelay_FallsBackToTheMessage()
    {
        var body = "{\"error\":{\"message\":\"Resource exhausted. Please retry in 28.9s.\"}}";

        Assert.Equal(TimeSpan.FromSeconds(28.9), ProviderErrorClassifier.ParseRetryDelay(body));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<html>502 Bad Gateway</html>")]
    [InlineData("{}")]
    [InlineData("""{"error":{"status":"RESOURCE_EXHAUSTED","message":"Quota exceeded"}}""")]
    [InlineData("""{"error":{"details":[{"@type":"type.googleapis.com/google.rpc.RetryInfo","retryDelay":"soon"}]}}""")]
    public void ParseRetryDelay_IsNullWhenTheProviderDoesNotSay(string? body)
    {
        Assert.Null(ProviderErrorClassifier.ParseRetryDelay(body));
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
