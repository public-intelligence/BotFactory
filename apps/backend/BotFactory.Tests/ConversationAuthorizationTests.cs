using BotFactory.Core.Auth;
using BotFactory.Core.Entities;
using BotFactory.Infrastructure.Tenancy;

namespace BotFactory.Tests;

public class ConversationAuthorizationTests
{
    private static Conversation Conv(string userId = "user-1", string orgId = "1") => new()
    {
        Id = 1,
        BotId = 1,
        UserId = userId,
        OrganizationId = orgId,
    };

    private static TenantContext Tenant(
        string userId = "user-1",
        string orgId = "1",
        params string[] permissions)
    {
        var t = new TenantContext();
        t.SetTenant(orgId, userId, "Test User");
        t.Permissions = [.. permissions];
        return t;
    }

    [Fact]
    public void CanView_AllowsOwnerWithBotChat()
    {
        var conv = Conv();
        var tenant = Tenant(permissions: [Permissions.BotChat]);
        Assert.True(ConversationAuthorization.CanView(conv, tenant));
    }

    [Fact]
    public void CanView_DeniesOtherUserWithBotChatOnly()
    {
        var conv = Conv(userId: "other");
        var tenant = Tenant(permissions: [Permissions.BotChat]);
        Assert.False(ConversationAuthorization.CanView(conv, tenant));
    }

    [Fact]
    public void CanView_AllowsConversationReadForNonOwner()
    {
        var conv = Conv(userId: "other");
        var tenant = Tenant(permissions: [Permissions.ConversationRead]);
        Assert.True(ConversationAuthorization.CanView(conv, tenant));
    }

    [Fact]
    public void CanView_DeniesCrossTenant()
    {
        var conv = Conv(orgId: "2");
        var tenant = Tenant(permissions: [Permissions.BotChat]);
        Assert.False(ConversationAuthorization.CanView(conv, tenant));
    }

    [Fact]
    public void CanView_AllowsAnonymousOwnerWithPublicChat()
    {
        // Anonymous public sessions: owner path with public:chat only.
        var conv = Conv(userId: "anon:abc123");
        var tenant = Tenant(userId: "anon:abc123", permissions: [Permissions.PublicChat]);
        Assert.True(ConversationAuthorization.CanView(conv, tenant));
    }

    [Fact]
    public void CanView_DeniesAnonymousNonOwnerWithPublicChat()
    {
        var conv = Conv(userId: "anon:abc123");
        var tenant = Tenant(userId: "anon:someone-else", permissions: [Permissions.PublicChat]);
        Assert.False(ConversationAuthorization.CanView(conv, tenant));
    }

    [Fact]
    public void CanView_DeniesAnonymousCrossTenant()
    {
        var conv = Conv(userId: "anon:abc123", orgId: "2");
        var tenant = Tenant(userId: "anon:abc123", orgId: "1", permissions: [Permissions.PublicChat]);
        Assert.False(ConversationAuthorization.CanView(conv, tenant));
    }

    [Fact]
    public void CanView_PublicChatDoesNotGrantStaffAccess()
    {
        // public:chat must never behave like a staff read permission.
        var conv = Conv(userId: "gob-user");
        var tenant = Tenant(userId: "anon:abc123", permissions: [Permissions.PublicChat]);
        Assert.False(ConversationAuthorization.CanView(conv, tenant));
    }
}
