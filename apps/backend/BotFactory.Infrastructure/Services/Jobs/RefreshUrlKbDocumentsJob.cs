using BotFactory.Core.Entities;
using BotFactory.Core.Services;
using BotFactory.Core.Tenancy;
using BotFactory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BotFactory.Infrastructure.Services.Jobs;

public class RefreshUrlKbDocumentsJob
{
    private readonly BotFactoryDbContext _db;
    private readonly IBackgroundJobService _jobService;
    private readonly ITenantContextSetter _tenantSetter;
    private readonly ILogger<RefreshUrlKbDocumentsJob> _logger;

    public RefreshUrlKbDocumentsJob(
        BotFactoryDbContext db,
        IBackgroundJobService jobService,
        ITenantContextSetter tenantSetter,
        ILogger<RefreshUrlKbDocumentsJob> logger)
    {
        _db = db;
        _jobService = jobService;
        _tenantSetter = tenantSetter;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var docs = await _db.KbDocuments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(d => d.SourceType == KbDocumentSourceType.Url && d.Status != DocumentStatus.Pending)
            .Select(d => new { d.Id, d.OrganizationId })
            .ToListAsync(cancellationToken);

        if (docs.Count == 0)
        {
            _logger.LogInformation("refresh-url-kb-documents: no URL documents to refresh");
            return;
        }

        foreach (var doc in docs)
        {
            _tenantSetter.SetTenant(doc.OrganizationId);
            _jobService.Enqueue<IngestUrlJob>(j => j.ExecuteAsync(doc.Id, doc.OrganizationId));
        }

        _logger.LogInformation("refresh-url-kb-documents: enqueued {Count} URL ingest jobs", docs.Count);
    }
}
