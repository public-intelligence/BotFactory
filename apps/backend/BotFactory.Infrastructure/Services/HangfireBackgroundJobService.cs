using System.Linq.Expressions;
using BotFactory.Core.Services;
using Hangfire;

namespace BotFactory.Infrastructure.Services;

public class HangfireBackgroundJobService : IBackgroundJobService
{
    private readonly IBackgroundJobClient _jobClient;

    public HangfireBackgroundJobService(IBackgroundJobClient jobClient)
    {
        _jobClient = jobClient;
    }

    public string Enqueue<T>(Expression<Func<T, Task>> methodCall) where T : notnull
        => _jobClient.Enqueue(methodCall);

    public string Schedule<T>(Expression<Func<T, Task>> methodCall, TimeSpan delay) where T : notnull
        => _jobClient.Schedule(methodCall, delay);
}
