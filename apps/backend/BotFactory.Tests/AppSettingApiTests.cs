using System.Net;
using System.Net.Http.Json;
using BotFactory.Core.DTOs;
using BotFactory.Tests.Infrastructure;

namespace BotFactory.Tests;

[Collection("Database")]
public class AppSettingApiTests
{
    private readonly BotFactoryWebApplicationFactory _factory;

    public AppSettingApiTests(BotFactoryWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task FullLifecycle_CreateReadUpdateDelete()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);

        // Create via bulk upsert
        var upsertRequest = new BulkUpsertAppSettingsRequest(
        [
            new UpsertAppSettingRequest("Stt", "sk_test_elevenlabs_key_1234")
        ]);

        var upsertResponse = await client.PutAsJsonAsync("/api/settings", upsertRequest);
        Assert.Equal(HttpStatusCode.OK, upsertResponse.StatusCode);

        var upserted = await upsertResponse.Content.ReadFromJsonAsync<List<AppSettingResponse>>();
        Assert.NotNull(upserted);
        Assert.Single(upserted);
        Assert.Equal("Stt", upserted[0].ServiceType);
        Assert.Equal("ElevenLabs", upserted[0].Provider);
        Assert.Equal("sk_te", upserted[0].KeyPrefix);
        Assert.True(upserted[0].HasKey);

        // Read — verify masked key returned, not plaintext
        var getResponse = await client.GetFromJsonAsync<List<AppSettingResponse>>("/api/settings");
        Assert.NotNull(getResponse);
        var sttSetting = getResponse.Find(s => s.ServiceType == "Stt");
        Assert.NotNull(sttSetting);
        Assert.Equal("sk_te", sttSetting.KeyPrefix);
        Assert.True(sttSetting.HasKey);

        // Update — change key
        var updateRequest = new BulkUpsertAppSettingsRequest(
        [
            new UpsertAppSettingRequest("Stt", "sk_updated_key_abcd")
        ]);
        var updateResponse = await client.PutAsJsonAsync("/api/settings", updateRequest);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var updated = await updateResponse.Content.ReadFromJsonAsync<List<AppSettingResponse>>();
        Assert.NotNull(updated);
        Assert.Equal("sk_up", updated[0].KeyPrefix);

        // Delete
        var deleteResponse = await client.DeleteAsync("/api/settings/Stt");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // Verify deleted
        var afterDelete = await client.GetFromJsonAsync<List<AppSettingResponse>>("/api/settings");
        Assert.NotNull(afterDelete);
        Assert.DoesNotContain(afterDelete, s => s.ServiceType == "Stt");
    }

    [Fact]
    public async Task BulkUpsert_AllFourServiceTypes()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);

        var request = new BulkUpsertAppSettingsRequest(
        [
            new UpsertAppSettingRequest("Llm", "gemini-key-llm-test1234"),
            new UpsertAppSettingRequest("Stt", "elevenlabs-key-stt-5678"),
            new UpsertAppSettingRequest("Tts", "elevenlabs-key-tts-9012"),
            new UpsertAppSettingRequest("Embeddings", "gemini-key-emb-test3456"),
        ]);

        var response = await client.PutAsJsonAsync("/api/settings", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var results = await response.Content.ReadFromJsonAsync<List<AppSettingResponse>>();
        Assert.NotNull(results);
        Assert.Equal(4, results.Count);

        Assert.Contains(results, s => s.ServiceType == "Llm" && s.Provider == "Gemini");
        Assert.Contains(results, s => s.ServiceType == "Stt" && s.Provider == "ElevenLabs");
        Assert.Contains(results, s => s.ServiceType == "Tts" && s.Provider == "ElevenLabs");
        Assert.Contains(results, s => s.ServiceType == "Embeddings" && s.Provider == "Gemini");
    }

    [Fact]
    public async Task TenantIsolation_OrgCannotAccessOtherOrgSettings()
    {
        var org1Client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory, organizationApplicationId: 300);
        var org2Client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory, organizationApplicationId: 400);

        // Org 1 saves a key
        var request = new BulkUpsertAppSettingsRequest(
        [
            new UpsertAppSettingRequest("Llm", "org1-secret-gemini-key!")
        ]);
        var response = await org1Client.PutAsJsonAsync("/api/settings", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Org 2 cannot see it
        var org2Settings = await org2Client.GetFromJsonAsync<List<AppSettingResponse>>("/api/settings");
        Assert.NotNull(org2Settings);
        Assert.DoesNotContain(org2Settings, s => s.ServiceType == "Llm");
    }

    [Fact]
    public async Task InvalidServiceType_Returns400()
    {
        var client = await TestAuthHelper.CreateAuthenticatedClientAsync(_factory);

        var request = new BulkUpsertAppSettingsRequest(
        [
            new UpsertAppSettingRequest("InvalidType", "some-key-1234")
        ]);

        var response = await client.PutAsJsonAsync("/api/settings", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/settings");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
