using System.Text.Json;
using BotFactory.Infrastructure.Telemetry;

namespace BotFactory.Tests;

public class AgentCloseClassifierTests
{
    private static JsonElement Parse(string json) =>
        JsonDocument.Parse(json).RootElement;

    [Theory]
    [InlineData("""{"reason":"10"}""")]
    [InlineData("""{"reason":"2"}""")]
    [InlineData("""{"reason":"client_request_leave"}""")]
    [InlineData("""{"reason":"participant_disconnected"}""")]
    [InlineData("""{"reason":"room_closed"}""")]
    public void IsBenignVoiceSessionEndCloseData_MatchesBenignReasons(string json)
    {
        Assert.True(AgentCloseClassifier.IsBenignVoiceSessionEndCloseData(Parse(json)));
    }

    [Theory]
    [InlineData("""{"reason":"10","error":"boom"}""")]
    [InlineData("""{"reason":"greeting_failed","error":"tts"}""")]
    [InlineData("""{"reason":"pipeline_error"}""")]
    [InlineData(null)]
    public void IsBenignVoiceSessionEndCloseData_RejectsFatalOrMissing(string? json)
    {
        JsonElement? data = json is null ? null : Parse(json);
        Assert.False(AgentCloseClassifier.IsBenignVoiceSessionEndCloseData(data));
    }
}
