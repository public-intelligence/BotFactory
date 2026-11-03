namespace BotFactory.Core.Tenancy;

public interface ITenantContext
{
    string OrganizationId { get; }
    string UserId { get; }
    string FullName { get; }
    string OrganizationName { get; }
    IReadOnlyList<string> Permissions { get; }
}
