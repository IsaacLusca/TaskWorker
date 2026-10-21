using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TaskWorker.Core.Options;
using TaskWorker.Core.Tasks;
using TaskWorker.Infrastructure.Persistence;
using Xunit.Sdk;

namespace TaskWorker.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class TaskProcessingTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string TestType = "test";

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Valid_task_reaches_completed_state()
    {
        var handler = new DelegateTaskHandler(
            TestType,
            (context, _) => Task.FromResult<string?>($$"""{"taskId":"{{context.TaskId}}"}"""));

        await using var worker = WorkerHost.Create(fixture.ConnectionString, handler);
        await worker.StartAsync();

        var task = await CreateTaskAsync();

        await WaitUntilAsync(
            async () => (await GetTaskAsync(task.Id)).State == TaskState.Completed,
            TimeSpan.FromSeconds(10),
            "task should reach Completed");

        var stored = await GetTaskAsync(task.Id);

        Assert.Equal(TaskState.Completed, stored.State);
        Assert.Equal(1, stored.Attempts);
        Assert.Null(stored.LastError);
        Assert.NotNull(stored.FinishedAt);
        Assert.Equal(1, await CountEffectsAsync(task.Id));
    }

    [Fact]
    public async Task Failing_task_is_recorded_and_does_not_block_other_tasks()
    {
        var handler = new DelegateTaskHandler(
            TestType,
            (context, _) =>
            {
                if (context.Payload.Contains("fail", StringComparison.Ordinal))
                {
                    throw new PermanentTaskException("Permanent test failure.");
                }

                return Task.FromResult<string?>("""{"ok":true}""");
            });

        var failingTask = await CreateTaskAsync("""{"value":"fail"}""");
        var successfulTask = await CreateTaskAsync("""{"value":"ok"}""");

        await using var worker = WorkerHost.Create(fixture.ConnectionString, handler);
        await worker.StartAsync();

        await WaitUntilAsync(
            async () =>
            {
                var failing = await GetTaskAsync(failingTask.Id);
                var successful = await GetTaskAsync(successfulTask.Id);

                return failing.State == TaskState.Failed
                       && successful.State == TaskState.Completed;
            },
            TimeSpan.FromSeconds(10),
            "both tasks should reach a terminal state");

        var storedFailure = await GetTaskAsync(failingTask.Id);
        var storedSuccess = await GetTaskAsync(successfulTask.Id);

        Assert.Equal(TaskState.Failed, storedFailure.State);
        Assert.Equal(1, storedFailure.Attempts);
        Assert.Contains("Permanent test failure.", storedFailure.LastError);
        Assert.Equal(0, await CountEffectsAsync(failingTask.Id));

        Assert.Equal(TaskState.Completed, storedSuccess.State);
        Assert.Equal(1, await CountEffectsAsync(successfulTask.Id));
    }

    [Fact]
    public async Task Transient_failure_is_retried_and_eventually_completes()
    {
        var handler = new DelegateTaskHandler(
            TestType,
            (context, _) => context.Attempt < 2
                ? throw new TransientTaskException("Transient test failure.")
                : Task.FromResult<string?>("""{"ok":true}"""));

        await using var worker = WorkerHost.Create(
            fixture.ConnectionString,
            handler,
            options =>
            {
                options.Retry.BaseDelay = TimeSpan.FromMilliseconds(500);
                options.Retry.MaxDelay = TimeSpan.FromSeconds(2);
            });

        var task = await CreateTaskAsync(maxAttempts: 3);
        await worker.StartAsync();

        await WaitUntilAsync(
            async () =>
            {
                var stored = await GetTaskAsync(task.Id);
                return stored.State == TaskState.Pending && stored.Attempts == 1;
            },
            TimeSpan.FromSeconds(10),
            "task should be scheduled for a retry");

        var scheduled = await GetTaskAsync(task.Id);

        Assert.NotNull(scheduled.NextAttemptAt);
        Assert.True(scheduled.NextAttemptAt > DateTimeOffset.UtcNow);

        await WaitUntilAsync(
            async () => (await GetTaskAsync(task.Id)).State == TaskState.Completed,
            TimeSpan.FromSeconds(10),
            "task should complete on the second attempt");

        var stored = await GetTaskAsync(task.Id);

        Assert.Equal(TaskState.Completed, stored.State);
        Assert.Equal(2, stored.Attempts);
        Assert.Equal(1, await CountEffectsAsync(task.Id));
    }

    [Fact]
    public async Task Task_exceeding_the_attempt_limit_becomes_failed()
    {
        var handler = new DelegateTaskHandler(
            TestType,
            (_, _) => throw new TransientTaskException("Always transient."));

        await using var worker = WorkerHost.Create(
            fixture.ConnectionString,
            handler,
            options =>
            {
                options.Retry.BaseDelay = TimeSpan.FromMilliseconds(100);
                options.Retry.MaxDelay = TimeSpan.FromMilliseconds(300);
            });

        var task = await CreateTaskAsync(maxAttempts: 2);
        await worker.StartAsync();

        await WaitUntilAsync(
            async () => (await GetTaskAsync(task.Id)).State == TaskState.Failed,
            TimeSpan.FromSeconds(10),
            "task should fail permanently after the attempt limit");

        var stored = await GetTaskAsync(task.Id);

        Assert.Equal(TaskState.Failed, stored.State);
        Assert.Equal(2, stored.Attempts);
        Assert.Contains("Always transient.", stored.LastError);
        Assert.NotNull(stored.FinishedAt);
        Assert.Equal(0, await CountEffectsAsync(task.Id));
    }

    [Fact]
    public async Task Two_workers_never_process_the_same_task()
    {
        var invocations = new ConcurrentDictionary<Guid, int>();

        var handler = new DelegateTaskHandler(
            TestType,
            async (context, cancellationToken) =>
            {
                invocations.AddOrUpdate(context.TaskId, 1, (_, count) => count + 1);
                await Task.Delay(25, cancellationToken);
                return null;
            });

        await using var workerOne = WorkerHost.Create(
            fixture.ConnectionString,
            handler,
            options => options.BatchSize = 3);

        await using var workerTwo = WorkerHost.Create(
            fixture.ConnectionString,
            handler,
            options => options.BatchSize = 3);

        await workerOne.StartAsync();
        await workerTwo.StartAsync();

        var tasks = new List<TaskItem>();

        for (var index = 0; index < 20; index++)
        {
            tasks.Add(await CreateTaskAsync());
        }

        var taskIds = tasks.Select(task => task.Id).ToArray();

        await WaitUntilAsync(
            async () =>
            {
                await using var db = fixture.CreateDbContext();
                var completed = await db.Tasks.CountAsync(task =>
                    taskIds.Contains(task.Id) && task.State == TaskState.Completed);

                return completed == tasks.Count;
            },
            TimeSpan.FromSeconds(30),
            "both workers should complete all tasks");

        foreach (var task in tasks)
        {
            var stored = await GetTaskAsync(task.Id);

            Assert.Equal(1, invocations.GetValueOrDefault(task.Id));
            Assert.Equal(1, stored.Attempts);
            Assert.Equal(1, await CountEffectsAsync(task.Id));
        }

        Assert.Equal(tasks.Count, invocations.Values.Sum());
    }

    [Fact]
    public async Task Graceful_shutdown_returns_in_flight_task_to_pending()
    {
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var handler = new DelegateTaskHandler(
            TestType,
            async (_, cancellationToken) =>
            {
                handlerStarted.TrySetResult();
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                return null;
            });

        await using var worker = WorkerHost.Create(
            fixture.ConnectionString,
            handler,
            options => options.PollInterval = TimeSpan.FromMilliseconds(25));

        await worker.StartAsync();
        var task = await CreateTaskAsync();

        await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        using (var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
        {
            await worker.StopAsync(cancellationTokenSource.Token);
        }

        var stored = await GetTaskAsync(task.Id);

        Assert.Equal(TaskState.Pending, stored.State);
        Assert.Equal(0, stored.Attempts);
        Assert.Null(stored.LockedAt);
        Assert.Null(stored.LockedBy);
        Assert.Null(stored.FinishedAt);
        Assert.Equal(0, await CountEffectsAsync(task.Id));
    }

    [Fact]
    public async Task Stale_processing_lease_is_recovered()
    {
        var handler = new DelegateTaskHandler(
            TestType,
            (_, _) => Task.FromResult<string?>(null));

        await using var worker = WorkerHost.Create(
            fixture.ConnectionString,
            handler,
            options =>
            {
                options.LeaseTimeout = TimeSpan.FromSeconds(2);
                options.LeaseRenewalInterval = TimeSpan.FromMilliseconds(200);
                options.LeaseRecoveryInterval = TimeSpan.FromMilliseconds(100);
            });

        var task = await CreateTaskAsync();

        await using (var db = fixture.CreateDbContext())
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE tasks SET state = 'Processing', attempts = 1, locked_at = now() - interval '10 minutes', locked_by = 'dead-worker' WHERE id = {task.Id}");
        }

        await worker.StartAsync();

        await WaitUntilAsync(
            async () => (await GetTaskAsync(task.Id)).State == TaskState.Completed,
            TimeSpan.FromSeconds(10),
            "stale task should be recovered and completed");

        var stored = await GetTaskAsync(task.Id);

        Assert.Equal(TaskState.Completed, stored.State);
        Assert.Equal(2, stored.Attempts);
    }

    [Fact]
    public async Task Active_task_is_not_stolen_while_its_lease_is_renewed()
    {
        var invocations = new ConcurrentDictionary<Guid, int>();
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var handler = new DelegateTaskHandler(
            TestType,
            async (context, cancellationToken) =>
            {
                invocations.AddOrUpdate(context.TaskId, 1, (_, count) => count + 1);
                handlerStarted.TrySetResult();
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                return null;
            });

        void ConfigureOptions(WorkerOptions options)
        {
            options.LeaseTimeout = TimeSpan.FromSeconds(1);
            options.LeaseRenewalInterval = TimeSpan.FromMilliseconds(200);
            options.LeaseRecoveryInterval = TimeSpan.FromMilliseconds(100);
        }

        await using var workerOne = WorkerHost.Create(fixture.ConnectionString, handler, ConfigureOptions);
        await using var workerTwo = WorkerHost.Create(fixture.ConnectionString, handler, ConfigureOptions);

        await workerOne.StartAsync();
        var task = await CreateTaskAsync();

        await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await workerTwo.StartAsync();

        await WaitUntilAsync(
            async () => (await GetTaskAsync(task.Id)).State == TaskState.Completed,
            TimeSpan.FromSeconds(15),
            "long-running task should complete without being stolen");

        var stored = await GetTaskAsync(task.Id);

        Assert.Equal(1, invocations.GetValueOrDefault(task.Id));
        Assert.Equal(1, stored.Attempts);
        Assert.Equal(1, await CountEffectsAsync(task.Id));
    }

    [Fact]
    public async Task Idempotency_key_returns_the_existing_task()
    {
        await using var db = fixture.CreateDbContext();
        var repository = new TaskRepository(db);

        var (first, firstCreated) = await repository.CreateAsync(
            TestType,
            "{}",
            maxAttempts: 3,
            idempotencyKey: "same-key",
            CancellationToken.None);

        var (second, secondCreated) = await repository.CreateAsync(
            TestType,
            "{}",
            maxAttempts: 3,
            idempotencyKey: "same-key",
            CancellationToken.None);

        Assert.True(firstCreated);
        Assert.False(secondCreated);
        Assert.Equal(first.Id, second.Id);

        await using var verificationDb = fixture.CreateDbContext();

        Assert.Equal(
            1,
            await verificationDb.Tasks.CountAsync(task => task.IdempotencyKey == "same-key"));
    }

    [Fact]
    public async Task Completing_the_same_task_twice_does_not_duplicate_the_effect()
    {
        await using var dataSource = new NpgsqlDataSourceBuilder(fixture.ConnectionString).Build();
        var queue = new TaskQueue(dataSource);
        var task = await CreateTaskAsync();

        var leases = await queue.ClaimBatchAsync("worker-a", batchSize: 10, CancellationToken.None);

        Assert.Contains(leases, lease => lease.Id == task.Id);

        var first = await queue.CompleteAsync(
            task.Id,
            "worker-a",
            """{"ok":true}""",
            CancellationToken.None);

        var second = await queue.CompleteAsync(
            task.Id,
            "worker-a",
            """{"ok":true}""",
            CancellationToken.None);

        Assert.True(first.TaskUpdated);
        Assert.True(first.EffectCreated);
        Assert.False(second.TaskUpdated);
        Assert.False(second.EffectCreated);
        Assert.Equal(1, await CountEffectsAsync(task.Id));
    }

    private async Task<TaskItem> CreateTaskAsync(
        string payload = "{}",
        int maxAttempts = 3)
    {
        await using var db = fixture.CreateDbContext();
        var repository = new TaskRepository(db);

        var (task, _) = await repository.CreateAsync(
            TestType,
            payload,
            maxAttempts,
            idempotencyKey: null,
            CancellationToken.None);

        return task;
    }

    private async Task<TaskItem> GetTaskAsync(Guid id)
    {
        await using var db = fixture.CreateDbContext();

        return await db.Tasks
            .AsNoTracking()
            .SingleAsync(task => task.Id == id);
    }

    private async Task<int> CountEffectsAsync(Guid taskId)
    {
        await using var db = fixture.CreateDbContext();

        return await db.Effects
            .AsNoTracking()
            .CountAsync(effect => effect.TaskId == taskId);
    }

    private static async Task WaitUntilAsync(
        Func<Task<bool>> condition,
        TimeSpan timeout,
        string because)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new XunitException($"Condition was not met within {timeout}: {because}");
    }
}
