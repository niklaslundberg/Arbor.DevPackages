namespace Arbor.DevPackages.Core.Retention;

public interface IRetentionScheduler
{
    Task ScheduleAsync(CancellationToken cancellationToken);
}
