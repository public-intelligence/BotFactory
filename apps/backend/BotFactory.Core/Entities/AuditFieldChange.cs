namespace BotFactory.Core.Entities;

public class AuditFieldChange
{
    public string Field { get; set; } = string.Empty;
    public object? OldValue { get; set; }
    public object? NewValue { get; set; }
}
