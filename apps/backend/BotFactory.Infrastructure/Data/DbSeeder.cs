using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BotFactory.Infrastructure.Data;

public static class DbSeeder
{
    public const string SuperAdminOrgId = "1";
    public const string SuperAdminUserId = "dev@botfactory.com";

    public static async Task SeedAsync(BotFactoryDbContext db, IEncryptionService encryption, ILogger logger)
    {
        var hasBot = await db.Bots
            .IgnoreQueryFilters()
            .AnyAsync(b => b.OrganizationId == SuperAdminOrgId);

        if (hasBot) return;

        // Insert directly to bypass SaveChangesAsync tenant override
        var publicId = PublicIdGenerator.NewId();
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO bots (OrganizationId, BotName, UrlSlug, PublishedState, MasterPrompt,
                              TopK, SilenceEndMs, NoSpeechTimeoutMs, MaxUtteranceMs,
                              EnableBargeIn, EnableTts, EnableTypingSound, EnableChat, EnableVoice,
                              PublicId, CreatedAt, UpdatedAt)
            VALUES ({SuperAdminOrgId}, 'Sample Bot', 'sample-bot', 'draft', '',
                    5, 700, 15000, 20000,
                    1, 1, 1, 0, 1,
                    {publicId}, GETUTCDATE(), GETUTCDATE())
            """);

        // Seed placeholder API keys for the super admin org. Real keys are set
        // via the Settings UI — never commit live provider keys here.
        const string elevenLabsKey = "sk-dev-placeholder-elevenlabs-key";
        const string geminiKey = "AIzaSy-dev-placeholder-gemini-key";

        var encElevenLabs = encryption.Encrypt(elevenLabsKey);
        var encGemini = encryption.Encrypt(geminiKey);

        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO app_settings (OrganizationId, ServiceType, Provider, EncryptedApiKey, KeyPrefix, CreatedAt, UpdatedAt)
            VALUES
                ({SuperAdminOrgId}, {nameof(ServiceType.Stt)}, {nameof(ProviderType.ElevenLabs)}, {encElevenLabs}, {elevenLabsKey.Substring(0, 5)}, GETUTCDATE(), GETUTCDATE()),
                ({SuperAdminOrgId}, {nameof(ServiceType.Llm)}, {nameof(ProviderType.Gemini)}, {encGemini}, {geminiKey.Substring(0, 5)}, GETUTCDATE(), GETUTCDATE())
            """);

        logger.LogInformation("Seeded super admin org ({OrgId}) with sample bot and API keys for user {UserId}",
            SuperAdminOrgId, SuperAdminUserId);
    }
}
