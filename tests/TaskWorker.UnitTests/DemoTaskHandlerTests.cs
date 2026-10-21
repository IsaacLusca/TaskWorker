using System.Text.Json;
using TaskWorker.Core.Tasks;
using TaskWorker.Worker;

namespace TaskWorker.UnitTests;

public sealed class DemoTaskHandlerTests
{
    private readonly DemoTaskHandler _handler = new();

    [Fact]
    public async Task Returns_a_result_containing_the_message()
    {
        var taskId = Guid.NewGuid();
        var context = CreateContext(taskId, """{"message":"hello"}""");

        var result = await _handler.HandleAsync(context, CancellationToken.None);

        Assert.NotNull(result);

        using var document = JsonDocument.Parse(result);

        Assert.Equal(taskId, document.RootElement.GetProperty("taskId").GetGuid());
        Assert.Equal("hello", document.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Transient_behavior_throws_a_transient_failure()
    {
        var context = CreateContext(Guid.NewGuid(), """{"behavior":"transient"}""");

        await Assert.ThrowsAsync<TransientTaskException>(() =>
            _handler.HandleAsync(context, CancellationToken.None));
    }

    [Fact]
    public async Task Transient_behavior_succeeds_after_fail_until_attempt()
    {
        var context = CreateContext(
            Guid.NewGuid(),
            """{"behavior":"transient","failUntilAttempt":1}""",
            attempt: 2);

        var result = await _handler.HandleAsync(context, CancellationToken.None);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Permanent_behavior_throws_a_permanent_failure()
    {
        var context = CreateContext(Guid.NewGuid(), """{"behavior":"permanent"}""");

        await Assert.ThrowsAsync<PermanentTaskException>(() =>
            _handler.HandleAsync(context, CancellationToken.None));
    }

    [Fact]
    public async Task Invalid_json_throws_a_permanent_failure()
    {
        var context = CreateContext(Guid.NewGuid(), "not-json");

        await Assert.ThrowsAsync<PermanentTaskException>(() =>
            _handler.HandleAsync(context, CancellationToken.None));
    }

    [Fact]
    public async Task Delay_respects_cancellation()
    {
        var context = CreateContext(Guid.NewGuid(), """{"delayMs":5000}""");
        using var cancellationTokenSource = new CancellationTokenSource();

        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _handler.HandleAsync(context, cancellationTokenSource.Token));
    }

    private static TaskContext CreateContext(
        Guid taskId,
        string payload,
        int attempt = 1) =>
        new(taskId, DemoTaskHandler.TaskType, payload, attempt, MaxAttempts: 3);
}
