using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskWorker.Core.Options;
using TaskWorker.Core.Processing;
using TaskWorker.Core.Tasks;
using TaskWorker.Infrastructure.Persistence;

namespace TaskWorker.Worker;

public sealed class TaskWorkerService(
    IServiceScopeFactory scopeFactory,
    IOptions<WorkerOptions> options,
    ILogger<TaskWorkerService> logger) : BackgroundService
{
    private readonly WorkerOptions _options = options.Value;
    private readonly string _workerId = $"{Environment.MachineName}-{Guid.NewGuid():N}";
    private DateTimeOffset _nextLeaseRecoveryAt = DateTimeOffset.MinValue;

    public string WorkerId => _workerId;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Worker {WorkerId} started. BatchSize {BatchSize}, PollInterval {PollInterval}, LeaseTimeout {LeaseTimeout}.",
            _workerId,
            _options.BatchSize,
            _options.PollInterval,
            _options.LeaseTimeout);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RecoverStaleLeasesIfDueAsync(stoppingToken);

                    var claimed = await ProcessBatchAsync(stoppingToken);

                    if (claimed == 0)
                    {
                        await Task.Delay(_options.PollInterval, stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    logger.LogError(
                        exception,
                        "Unexpected error in the worker loop. Worker {WorkerId}.",
                        _workerId);

                    try
                    {
                        await Task.Delay(_options.PollInterval, stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }
        finally
        {
            logger.LogInformation("Worker {WorkerId} stopped.", _workerId);
        }
    }

    private async Task<int> ProcessBatchAsync(CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        var queue = scope.ServiceProvider.GetRequiredService<TaskQueue>();
        var leases = await queue.ClaimBatchAsync(_workerId, _options.BatchSize, stoppingToken);

        if (leases.Count == 0)
        {
            return 0;
        }

        logger.LogInformation(
            "Worker {WorkerId} claimed {TaskCount} task(s).",
            _workerId,
            leases.Count);

        var processor = scope.ServiceProvider.GetRequiredService<TaskProcessor>();

        for (var index = 0; index < leases.Count; index++)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                await ReleaseRemainingAsync(queue, leases, index);
                break;
            }

            try
            {
                await processor.ProcessAsync(leases[index], _workerId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                await ReleaseRemainingAsync(queue, leases, index);
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Unhandled error while processing task {TaskId}. Worker {WorkerId}.",
                    leases[index].Id,
                    _workerId);
            }
        }

        return leases.Count;
    }

    private async Task ReleaseRemainingAsync(
        TaskQueue queue,
        IReadOnlyList<TaskLease> leases,
        int startIndex)
    {
        for (var index = startIndex; index < leases.Count; index++)
        {
            var lease = leases[index];

            try
            {
                var released = await queue.ReleaseAsync(
                    lease.Id,
                    _workerId,
                    CancellationToken.None);

                if (released)
                {
                    logger.LogInformation(
                        "Task {TaskId} returned to {State} during graceful shutdown. Worker {WorkerId}.",
                        lease.Id,
                        TaskState.Pending.ToString(),
                        _workerId);
                }
                else
                {
                    logger.LogWarning(
                        "Task {TaskId} could not be released during shutdown because its lease was lost. Worker {WorkerId}.",
                        lease.Id,
                        _workerId);
                }
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Failed to release task {TaskId} during shutdown; lease recovery will requeue it. Worker {WorkerId}.",
                    lease.Id,
                    _workerId);
            }
        }
    }

    private async Task RecoverStaleLeasesIfDueAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        if (now < _nextLeaseRecoveryAt)
        {
            return;
        }

        _nextLeaseRecoveryAt = now + _options.LeaseRecoveryInterval;

        await using var scope = scopeFactory.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<TaskQueue>();
        var recovered = await queue.RecoverStaleAsync(_options.LeaseTimeout, cancellationToken);

        if (recovered > 0)
        {
            logger.LogWarning(
                "Recovered {RecoveredCount} task(s) with expired processing leases. Worker {WorkerId}.",
                recovered,
                _workerId);
        }
    }
}
