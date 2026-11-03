using BotFactory.Core.Tenancy;

namespace BotFactory.Infrastructure.Tenancy;

public class TenantContext : ITenantContext, ITenantContextSetter
{
    public string OrganizationId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string OrganizationName { get; set; } = string.Empty;
    public List<string> Permissions { get; set; } = [];
    IReadOnlyList<string> ITenantContext.Permissions => Permissions;

    public void SetTenant(string organizationId, string userId = "", string fullName = "System")
    {
        OrganizationId = organizationId;
        UserId = userId;
        FullName = fullName;
    }
}
