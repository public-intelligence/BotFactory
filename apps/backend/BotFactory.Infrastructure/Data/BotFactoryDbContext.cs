using System.Reflection;
using BotFactory.Core.Entities;
using BotFactory.Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace BotFactory.Infrastructure.Data;

public class BotFactoryDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    public BotFactoryDbContext(DbContextOptions<BotFactoryDbContext> options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<Bot> Bots => Set<Bot>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<OrgBranding> OrgBrandings => Set<OrgBranding>();
    public DbSet<KbDocument> KbDocuments => Set<KbDocument>();
    public DbSet<KbChunk> KbChunks => Set<KbChunk>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<KeyValueEntry> KeyValueEntries => Set<KeyValueEntry>();
    public DbSet<TelemetryEvent> TelemetryEvents => Set<TelemetryEvent>();
    public DbSet<ToolConnection> ToolConnections => Set<ToolConnection>();
    public DbSet<ToolDefinition> ToolDefinitions => Set<ToolDefinition>();
    public DbSet<BotTool> BotTools => Set<BotTool>();
    public DbSet<ToolExecution> ToolExecutions => Set<ToolExecution>();

    private bool _isAuditing;
    private static readonly HashSet<string> SkippedProperties = ["Id", "OrganizationId", "CreatedAt", "UpdatedAt"];

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BotFactoryDbContext).Assembly);

        // Apply global query filters for multi-tenancy on all BaseEntity subclasses
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(BaseEntity).IsAssignableFrom(entityType.ClrType)) continue;

            var method = typeof(BotFactoryDbContext)
                .GetMethod(nameof(ApplyTenantFilter), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .MakeGenericMethod(entityType.ClrType);

            method.Invoke(this, [modelBuilder]);
        }
    }

    private void ApplyTenantFilter<T>(ModelBuilder modelBuilder) where T : BaseEntity
    {
        modelBuilder.Entity<T>().HasQueryFilter(e => e.OrganizationId == _tenantContext.OrganizationId);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.OrganizationId = _tenantContext.OrganizationId;
                    entry.Entity.CreatedAt = DateTime.UtcNow;
                    entry.Entity.UpdatedAt = DateTime.UtcNow;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = DateTime.UtcNow;
                    break;
            }
        }

        if (_isAuditing)
            return await base.SaveChangesAsync(cancellationToken);

        _isAuditing = true;
        try
        {
            var auditableEntries = ChangeTracker.Entries<IAuditable>()
                .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .ToList();

            var deferredAdds = new List<(EntityEntry entry, List<AuditFieldChange> changes)>();

            foreach (var entry in auditableEntries)
            {
                var changes = CollectChanges(entry);
                if (changes.Count == 0) continue;

                if (entry.State == EntityState.Added)
                    deferredAdds.Add((entry, changes));
                else
                    AuditLogs.Add(CreateAuditLog(entry, changes));
            }

            var result = await base.SaveChangesAsync(cancellationToken);

            if (deferredAdds.Count > 0)
            {
                foreach (var (entry, changes) in deferredAdds)
                    AuditLogs.Add(CreateAuditLog(entry, changes));

                await base.SaveChangesAsync(cancellationToken);
            }

            return result;
        }
        finally
        {
            _isAuditing = false;
        }
    }

    private static List<AuditFieldChange> CollectChanges(EntityEntry entry)
    {
        var changes = new List<AuditFieldChange>();

        foreach (var prop in entry.Properties)
        {
            if (prop.Metadata.IsPrimaryKey()) continue;
            if (SkippedProperties.Contains(prop.Metadata.Name)) continue;

            var isMasked = prop.Metadata.PropertyInfo?.GetCustomAttribute<AuditMaskAttribute>() is not null;

            switch (entry.State)
            {
                case EntityState.Added:
                {
                    var newVal = prop.CurrentValue;
                    if (newVal is null) continue;
                    changes.Add(new AuditFieldChange
                    {
                        Field = prop.Metadata.Name,
                        OldValue = null,
                        NewValue = isMasked ? "***" : newVal,
                    });
                    break;
                }
                case EntityState.Deleted:
                {
                    var oldVal = prop.OriginalValue;
                    if (oldVal is null) continue;
                    changes.Add(new AuditFieldChange
                    {
                        Field = prop.Metadata.Name,
                        OldValue = isMasked ? "***" : oldVal,
                        NewValue = null,
                    });
                    break;
                }
                case EntityState.Modified:
                {
                    if (!prop.IsModified) continue;
                    var oldVal = prop.OriginalValue;
                    var newVal = prop.CurrentValue;
                    if (Equals(oldVal, newVal)) continue;
                    changes.Add(new AuditFieldChange
                    {
                        Field = prop.Metadata.Name,
                        OldValue = isMasked ? "***" : oldVal,
                        NewValue = isMasked ? "***" : newVal,
                    });
                    break;
                }
            }
        }

        return changes;
    }

    private AuditLog CreateAuditLog(EntityEntry entry, List<AuditFieldChange> changes)
    {
        var baseEntity = (BaseEntity)entry.Entity;
        return new AuditLog
        {
            EntityType = entry.Entity.GetType().Name,
            EntityId = baseEntity.Id,
            Action = entry.State.ToString(),
            Changes = changes,
            UserId = _tenantContext.UserId,
            UserName = _tenantContext.FullName,
            OrganizationId = _tenantContext.OrganizationId,
        };
    }
}
