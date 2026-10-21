using TaskWorker.Core.Tasks;

namespace TaskWorker.UnitTests;

public sealed class TaskItemTests
{
    [Fact]
    public void New_task_starts_pending_with_one_scheduled_attempt()
    {
        var now = DateTimeOffset.UtcNow;

        var task = new TaskItem("demo", "{}", maxAttempts: 3, idempotencyKey: null, createdAt: now);

        Assert.Equal(TaskState.Pending, task.State);
        Assert.Equal(0, task.Attempts);
        Assert.Equal(now, task.CreatedAt);
        Assert.Equal(now, task.NextAttemptAt);
        Assert.Null(task.LastError);
        Assert.Null(task.FinishedAt);
    }

    [Fact]
    public void Max_attempts_must_be_positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TaskItem("demo", "{}", maxAttempts: 0, idempotencyKey: null, createdAt: DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Idempotency_key_is_limited_to_200_characters()
    {
        Assert.Throws<ArgumentException>(() =>
            new TaskItem(
                "demo",
                "{}",
                maxAttempts: 1,
                idempotencyKey: new string('a', 201),
                createdAt: DateTimeOffset.UtcNow));
    }
}
