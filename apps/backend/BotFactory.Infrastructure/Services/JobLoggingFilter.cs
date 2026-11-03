using Hangfire.Common;
using Hangfire.Server;
using Hangfire.States;
using Microsoft.Extensions.Logging;

namespace BotFactory.Infrastructure.Services;

public class JobLoggingFilter : JobFilterAttribute, IServerFilter, IElectStateFilter
{
    private readonly ILogger _logger;

    public JobLoggingFilter(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<JobLoggingFilter>();
    }

    public void OnPerforming(PerformingContext context)
    {
        _logger.LogInformation("Job {JobId} starting: {JobType}.{Method}",
            context.BackgroundJob.Id,
            context.BackgroundJob.Job.Type.Name,
            context.BackgroundJob.Job.Method.Name);
    }

    public void OnPerformed(PerformedContext context)
    {
        _logger.LogInformation("Job {JobId} completed: {JobType}.{Method}",
            context.BackgroundJob.Id,
            context.BackgroundJob.Job.Type.Name,
            context.BackgroundJob.Job.Method.Name);
    }

    public void OnStateElection(ElectStateContext context)
    {
        if (context.CandidateState is FailedState failedState)
        {
            _logger.LogError(failedState.Exception,
                "Job {JobId} failed: {JobType}.{Method}",
                context.BackgroundJob.Id,
                context.BackgroundJob.Job.Type.Name,
                context.BackgroundJob.Job.Method.Name);
        }
    }
}
