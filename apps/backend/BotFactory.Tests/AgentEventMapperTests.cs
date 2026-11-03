using System.Text.Json;
using BotFactory.Core;
using BotFactory.Core.Entities;
using BotFactory.Infrastructure.Telemetry;

namespace BotFactory.Tests;

public class AgentEventMapperTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Map_ConversationStarted_ReturnsLifecycle()
    {
        var emit = AgentEventMapper.Map("ConversationStarted", 1, 2, DateTime.UtcNow, null);

        Assert.NotNull(emit);
        Assert.Equal("conversation_started", emit!.EventName);
        Assert.Equal("lifecycle", emit.Stage);
        Assert.Equal(EventSource.Agent, emit.Source);
    }

    [Fact]
    public void Map_Close_ReturnsNull_CloseAsyncOwnsLifecycle()
    {
        var emit = AgentEventMapper.Map("Close", 1, 2, DateTime.UtcNow, Parse("""{"reason":"user_left"}"""));
        Assert.Null(emit);
    }

    [Theory]
    [InlineData("user_left", ConversationClosedReasons.UserAborted)]
    [InlineData("timeout", ConversationClosedReasons.Timeout)]
    [InlineData("no_speech_timeout", ConversationClosedReasons.Timeout)]
    [InlineData("shutdown", ConversationClosedReasons.Timeout)]
    [InlineData("user navigated away", ConversationClosedReasons.Timeout)]
    public void MapCloseReason_MapsToConversationClosedReasons(string reason, string expected)
    {
        var result = AgentEventMapper.MapCloseReason(Parse($$$"""{"reason":"{{{reason}}}"}"""));
        Assert.Equal(expected, result);
    }

    [Fact]
    public void MapCloseReason_MissingData_DefaultsToUserAborted()
    {
        Assert.Equal(ConversationClosedReasons.UserAborted, AgentEventMapper.MapCloseReason(null));
    }

    [Fact]
    public void Map_Error_WithEnrichedPayload_UsesOperatorSummary()
    {
        var emit = AgentEventMapper.Map("Error", 1, 2, null, Parse(
            """{"error":"raw","operatorMessage":"ElevenLabs: saldo agotado","errorCode":"elevenlabs_quota_exceeded","source":"tts-elevenlabs"}"""));

        Assert.NotNull(emit);
        Assert.Equal("tts_failed", emit!.EventName);
        Assert.Equal("ElevenLabs: saldo agotado", emit.Summary);
    }

    [Fact]
    public void Map_Error_WithStringSource_MapsToFailed()
    {
        var emit = AgentEventMapper.Map("Error", 1, 2, null, Parse("""{"error":"boom","source":"stt-elevenlabs"}"""));

        Assert.NotNull(emit);
        Assert.Equal("stt_failed", emit!.EventName);
        Assert.Equal("stt", emit.Stage);
        Assert.Equal("boom", emit.ErrorMessage);
    }

    [Fact]
    public void Map_Error_WithLabelObjectSource_MapsToFailed()
    {
        var emit = AgentEventMapper.Map("Error", 1, 2, null, Parse("""{"error":"boom","source":{"label":"tts-elevenlabs"}}"""));

        Assert.NotNull(emit);
        Assert.Equal("tts_failed", emit!.EventName);
    }

    [Fact]
    public void Map_Error_WithLlmSource_MapsToLlmError()
    {
        var emit = AgentEventMapper.Map("Error", 1, 2, null, Parse("""{"error":"boom","source":"gemini"}"""));

        Assert.NotNull(emit);
        Assert.Equal("llm_error", emit!.EventName);
    }

    [Fact]
    public void Map_MetricsCollected_SttMetrics_ExtractsTranscriptionDelay()
    {
        var data = Parse("""{"metrics":{"type":"stt_metrics","transcriptionDelayMs":250,"durationMs":1000}}""");
        var emit = AgentEventMapper.Map("MetricsCollected", 1, 2, null, data);

        Assert.NotNull(emit);
        Assert.Equal("stt", emit!.Stage);
        Assert.Equal(250, emit.LatencyMs);
    }

    [Fact]
    public void Map_MetricsCollected_LlmMetrics_ExtractsTtft()
    {
        var data = Parse("""{"metrics":{"type":"llm_metrics","ttftMs":410,"inferenceDurationTotalMs":1500}}""");
        var emit = AgentEventMapper.Map("MetricsCollected", 1, 2, null, data);

        Assert.Equal("llm", emit!.Stage);
        Assert.Equal(410, emit.LatencyMs);
    }

    [Fact]
    public void Map_MetricsCollected_TtsMetrics_ExtractsTtfb()
    {
        var data = Parse("""{"metrics":{"type":"tts_metrics","ttfbMs":180}}""");
        var emit = AgentEventMapper.Map("MetricsCollected", 1, 2, null, data);

        Assert.Equal("tts", emit!.Stage);
        Assert.Equal(180, emit.LatencyMs);
    }

    [Fact]
    public void Map_UserInputTranscribed_Partial_ReturnsNull()
    {
        var emit = AgentEventMapper.Map("UserInputTranscribed", 1, 2, null, Parse("""{"isFinal":false,"transcript":"hola"}"""));
        Assert.Null(emit);
    }

    [Fact]
    public void Map_UserInputTranscribed_Final_ReturnsEvent()
    {
        var emit = AgentEventMapper.Map("UserInputTranscribed", 1, 2, null, Parse("""{"isFinal":true,"transcript":"hola"}"""));
        Assert.NotNull(emit);
        Assert.Equal("user_input_transcribed", emit!.EventName);
        Assert.Equal("stt", emit.Stage);
    }

    [Fact]
    public void Map_BargeIn_ReturnsLifecycle()
    {
        var emit = AgentEventMapper.Map("BargeIn", 1, 2, null, null);
        Assert.Equal("barge_in", emit!.EventName);
    }

    [Fact]
    public void Map_TurnStarted_ExtractsTurnId()
    {
        var turnId = Guid.NewGuid();
        var emit = AgentEventMapper.Map(
            "TurnStarted", 1, 2, null,
            Parse($$"""{"turnId":"{{turnId}}"}"""));

        Assert.Equal("turn_started", emit!.EventName);
        Assert.Equal("lifecycle", emit.Stage);
        Assert.Equal(EventSource.Agent, emit.Source);
        Assert.Equal(turnId, emit.TurnId);
    }

    [Fact]
    public void Map_TurnCompleted_ExtractsTurnIdAndSummarizes()
    {
        var turnId = Guid.NewGuid();
        var emit = AgentEventMapper.Map(
            "TurnCompleted", 1, 2, null,
            Parse($$"""{"turnId":"{{turnId}}","eouMs":80,"sttMs":250,"llmMs":1380,"ttsMs":540}"""));

        Assert.Equal("turn_completed", emit!.EventName);
        Assert.Equal("pipeline", emit.Stage);
        Assert.Equal(EventSource.Agent, emit.Source);
        Assert.Equal(turnId, emit.TurnId);
        Assert.Equal("eou=80ms stt=250ms llm=1380ms tts=540ms", emit.Summary);
    }

    [Fact]
    public void Map_TurnCompleted_InvalidTurnId_LeavesTurnIdNull()
    {
        var emit = AgentEventMapper.Map("TurnCompleted", 1, 2, null, Parse("""{"turnId":"not-a-guid","sttMs":10}"""));
        Assert.Equal("turn_completed", emit!.EventName);
        Assert.Null(emit.TurnId);
        Assert.Equal("stt=10ms", emit.Summary);
    }

    [Fact]
    public void Map_NetworkStateChanged_Reconnecting_MapsToDegraded()
    {
        var emit = AgentEventMapper.Map("NetworkStateChanged", 1, 2, null, Parse("""{"state":"reconnecting"}"""));
        Assert.Equal("network_degraded", emit!.EventName);
    }

    [Fact]
    public void Map_NetworkStateChanged_Reconnected_MapsToRecovered()
    {
        var emit = AgentEventMapper.Map("NetworkStateChanged", 1, 2, null, Parse("""{"state":"reconnected"}"""));
        Assert.Equal("network_recovered", emit!.EventName);
    }

    [Fact]
    public void Map_UnknownEventType_FallsThroughWithSnakeCaseName()
    {
        var emit = AgentEventMapper.Map("SomeBrandNewThing", 1, 2, null, null);
        Assert.NotNull(emit);
        Assert.Equal("some_brand_new_thing", emit!.EventName);
        Assert.Null(emit.Stage);
    }

    [Fact]
    public void Map_EmptyEventType_ReturnsNull()
    {
        Assert.Null(AgentEventMapper.Map("", 1, 2, null, null));
        Assert.Null(AgentEventMapper.Map(null!, 1, 2, null, null));
    }
}
