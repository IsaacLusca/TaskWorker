using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskWorker.Core.Options;
using TaskWorker.Core.Processing;
using TaskWorker.Core.Tasks;
using TaskWorker.Infrastructure.Persistence;

namespace TaskWorker.Worker;

public sealed class TaskProcessor(
    TaskQueue queue,
    TaskHandlerRegistry handlers,
    RetrySchedule retrySchedule,
    IOptions<WorkerOptions> options,
    ILogger<TaskProcessor> logger)
{
    private readonly WorkerOptions _options = options.Value;

    public async Task ProcessAsync(
        TaskLease lease,
        string workerId,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var handler = handlers.Find(lease.Type);
        var leaseLost = false;

        using var leaseLostCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeatTask = RenewLeaseAsync(
            lease.Id,
            workerId,
            leaseLostCts,
            () => leaseLost = true,
            heartbeatCts.Token);

        logger.LogInformation(
            "Task {TaskId} processing started with state {State}. TaskType {TaskType}, Attempt {Attempt}/{MaxAttempts}, Worker {WorkerId}.",
            lease.Id,
            TaskState.Processing.ToString(),
            lease.Type,
            lease.Attempt,
            lease.MaxAttempts,
            workerId);

        try
        {
            if (handler is null)
            {
                throw new PermanentTaskException(
                    $"No handler registered for task type '{lease.Type}'.");
            }

            var context = new TaskContext(
                lease.Id,
                lease.Type,
                lease.Payload,
                lease.Attempt,
                lease.MaxAttempts);

            var result = await handler.HandleAsync(context, leaseLostCts.Token);

            var completion = await queue.CompleteAsync(
                lease.Id,
                workerId,
                result,
                CancellationToken.None);

            if (!completion.TaskUpdated)
            {
                logger.LogWarning(
                    "Task {TaskId} was processed but the lease was no longer owned by worker {WorkerId}. State {State}.",
                    lease.Id,
                    workerId,
                    TaskState.Processing.ToString());
                return;
            }

            logger.LogInformation(
                "Task {TaskId} transitioned to {State} in {DurationMs} ms. EffectCreated {EffectCreated}, Worker {WorkerId}.",
                lease.Id,
                TaskState.Completed.ToString(),
                ElapsedMilliseconds(started),
                completion.EffectCreated,
                workerId);
        }
        catch (OperationCanceledException) when (leaseLost && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                "Task {TaskId} processing cancelled because the lease was lost. State {State}.",
                lease.Id,
                TaskState.Processing.ToString());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation(
                "Task {TaskId} processing interrupted by shutdown; returning it to the queue. State {State}, Worker {WorkerId}.",
                lease.Id,
                TaskState.Processing.ToString(),
                workerId);

            throw;
        }
        catch (TaskFailureException exception)
        {
            await HandleFailureAsync(
                lease,
                workerId,
                exception.Message,
                exception.IsPermanent,
                started);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Unexpected error processing task {TaskId}; treating it as transient. Worker {WorkerId}.",
                lease.Id,
                workerId);

            await HandleFailureAsync(
                lease,
                workerId,
                exception.Message,
                permanent: false,
                started);
        }
        finally
        {
            await heartbeatCts.CancelAsync();

            try
            {
                await heartbeatTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task HandleFailureAsync(
        TaskLease lease,
        string workerId,
        string reason,
        bool permanent,
        long started)
    {
        var delay = retrySchedule.GetDelay(lease.Attempt);

        TaskFailureResult? result;

        try
        {
            result = await queue.RecordFailureAsync(
                lease.Id,
                workerId,
                Truncate(reason),
                permanent,
                delay,
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to persist failure for task {TaskId}; the lease will expire and the task will be recovered. Worker {WorkerId}.",
                lease.Id,
                workerId);
            return;
        }

        if (result is null)
        {
            logger.LogWarning(
                "Failure of task {TaskId} was not recorded because the lease was lost. Worker {WorkerId}.",
                lease.Id,
                workerId);
            return;
        }

        if (result.State == TaskState.Failed)
        {
            logger.LogError(
                "Task {TaskId} transitioned to {State} after {Attempts} attempt(s) in {DurationMs} ms. FailureReason {FailureReason}, Worker {WorkerId}.",
                lease.Id,
                result.State.ToString(),
                result.Attempts,
                ElapsedMilliseconds(started),
                reason,
                workerId);
            return;
        }

        logger.LogWarning(
            "Task {TaskId} transitioned to {State} after attempt {Attempts} in {DurationMs} ms. FailureReason {FailureReason}, NextAttemptAt {NextAttemptAt}, Worker {WorkerId}.",
            lease.Id,
            result.State.ToString(),
            result.Attempts,
            ElapsedMilliseconds(started),
            reason,
            result.NextAttemptAt,
            workerId);
    }

    private async Task RenewLeaseAsync(
        Guid taskId,
        string workerId,
        CancellationTokenSource leaseLostCts,
        Action onLeaseLost,
        CancellationToken heartbeatToken)
    {
        var renewalInterval = _options.LeaseRenewalInterval;

        using var timer = new PeriodicTimer(renewalInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(heartbeatToken))
            {
                try
                {
                    if (await queue.RenewLeaseAsync(taskId, workerId, heartbeatToken))
                    {
                        continue;
                    }

                    onLeaseLost();

                    logger.LogWarning(
                        "Lease for task {TaskId} is no longer owned by worker {WorkerId}; cancelling processing.",
                        taskId,
                        workerId);

                    await leaseLostCts.CancelAsync();
                    return;
                }
                catch (OperationCanceledException) when (heartbeatToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    logger.LogWarning(
                        exception,
                        "Failed to renew lease for task {TaskId}; trying again. Worker {WorkerId}.",
                        taskId,
                        workerId);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static long ElapsedMilliseconds(long startedTimestamp) =>
        (long)Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds;

    private static string Truncate(string reason)
    {
        var message = string.IsNullOrWhiteSpace(reason) ? "Unknown failure." : reason;

        return message.Length <= TaskItem.MaxErrorLength
            ? message
            : message[..TaskItem.MaxErrorLength];
    }
}
