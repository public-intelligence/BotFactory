namespace BotFactory.Core.Tenancy;

public interface ITenantContextSetter
{
    void SetTenant(string organizationId, string userId = "", string fullName = "System");
}
