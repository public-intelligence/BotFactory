using System.Linq.Expressions;

namespace BotFactory.Core.Services;

public interface IBackgroundJobService
{
    string Enqueue<T>(Expression<Func<T, Task>> methodCall) where T : notnull;
    string Schedule<T>(Expression<Func<T, Task>> methodCall, TimeSpan delay) where T : notnull;
}
