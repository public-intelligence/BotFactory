using System.Text.Json;
using BotFactory.Api.Helpers;
using BotFactory.Core.Entities;
using BotFactory.Infrastructure.Services;

namespace BotFactory.Tests;

// room.metadata reaches every participant, anonymous visitors included; only the
// dispatched worker receives the dispatch metadata. These tests pin that boundary.
public class LiveKitRoomRequestTests
{
    private const string ApiKey = "sk-test-secret-value";

    private static Bot NewBot() => new()
    {
        Id = 7,
        VoiceId = "voice-1",
        GreetingMessage = "Hola",
        TranscriptionVocabulary = "Aurora\nPunto Ciudadano\nQuisqueya",
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoomMetadata_NeverCarriesTheApiKey(bool isResume)
    {
        var metadata = VoiceChatRoomHelper.BuildRoomMetadata(NewBot(), 42, isResume);

        var request = LiveKitService.BuildCreateRoomRequest(
            metadata, new AgentJobMetadata(ApiKey), VoiceChatRoomHelper.AgentName, "42");

        Assert.DoesNotContain(ApiKey, request.Metadata);
        Assert.DoesNotContain("elevenLabsApiKey", request.Metadata, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dispatch_CarriesTheApiKeyForTheAgentOnly()
    {
        var metadata = VoiceChatRoomHelper.BuildRoomMetadata(NewBot(), 42);

        var request = LiveKitService.BuildCreateRoomRequest(
            metadata, new AgentJobMetadata(ApiKey), VoiceChatRoomHelper.AgentName, "42");

        var dispatch = Assert.Single(request.Agents);
        Assert.Equal(VoiceChatRoomHelper.AgentName, dispatch.AgentName);
        using var json = JsonDocument.Parse(dispatch.Metadata);
        Assert.Equal(ApiKey, json.RootElement.GetProperty("elevenLabsApiKey").GetString());
    }

    [Fact]
    public void RoomMetadata_KeepsTheContractTheAgentParses()
    {
        var metadata = VoiceChatRoomHelper.BuildRoomMetadata(NewBot(), 42, isResume: true);

        var request = LiveKitService.BuildCreateRoomRequest(
            metadata, new AgentJobMetadata(ApiKey), VoiceChatRoomHelper.AgentName, "42-r1");

        Assert.Equal("42-r1", request.Name);
        using var json = JsonDocument.Parse(request.Metadata);
        var root = json.RootElement;
        Assert.Equal(7, root.GetProperty("botId").GetInt32());
        Assert.Equal(42, root.GetProperty("conversationId").GetInt32());
        Assert.Equal("voice-1", root.GetProperty("voiceId").GetString());
        Assert.Equal("Hola", root.GetProperty("greetingMessage").GetString());
        Assert.True(root.GetProperty("isResume").GetBoolean());
        // The words the recogniser should favour travel as a list the agent hands to the provider.
        Assert.Equal(
            ["Aurora", "Punto Ciudadano", "Quisqueya"],
            root.GetProperty("transcriptionVocabulary").EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    [Fact]
    public void RoomMetadata_WithoutVocabulary_SendsAnEmptyList()
    {
        var bot = NewBot();
        bot.TranscriptionVocabulary = string.Empty;

        var request = LiveKitService.BuildCreateRoomRequest(
            VoiceChatRoomHelper.BuildRoomMetadata(bot, 42), new AgentJobMetadata(ApiKey), VoiceChatRoomHelper.AgentName, "42");

        using var json = JsonDocument.Parse(request.Metadata);
        Assert.Equal(0, json.RootElement.GetProperty("transcriptionVocabulary").GetArrayLength());
    }
}
