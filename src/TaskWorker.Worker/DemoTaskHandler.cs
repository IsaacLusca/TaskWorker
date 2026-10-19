using System.Text.Json;
using TaskWorker.Core.Tasks;

namespace TaskWorker.Worker;

public sealed class DemoTaskHandler : ITaskHandler
{
    public const string TaskType = "demo";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public string Type => TaskType;

    public async Task<string?> HandleAsync(
        TaskContext context,
        CancellationToken cancellationToken)
    {
        DemoPayload payload;

        try
        {
            payload = JsonSerializer.Deserialize<DemoPayload>(context.Payload, JsonOptions)
                ?? new DemoPayload();
        }
        catch (JsonException exception)
        {
            throw new PermanentTaskException("Demo task payload is not valid JSON.", exception);
        }

        if (payload.DelayMs is < 0 or > 60_000)
        {
            throw new PermanentTaskException("Demo delay must be between 0 and 60000 milliseconds.");
        }

        if (payload.DelayMs > 0)
        {
            await Task.Delay(payload.DelayMs, cancellationToken);
        }

        var behavior = payload.Behavior?.ToLowerInvariant() ?? "success";

        switch (behavior)
        {
            case "success":
                break;

            case "transient":
                if (context.Attempt <= (payload.FailUntilAttempt ?? int.MaxValue))
                {
                    throw new TransientTaskException(
                        $"Transient demo failure on attempt {context.Attempt}.");
                }

                break;

            case "permanent":
                throw new PermanentTaskException("Permanent demo failure.");

            default:
                throw new PermanentTaskException($"Unknown demo behavior '{payload.Behavior}'.");
        }

        return JsonSerializer.Serialize(
            new
            {
                taskId = context.TaskId,
                message = payload.Message ?? "demo"
            },
            JsonOptions);
    }

    private sealed record DemoPayload
    {
        public string? Message { get; init; }

        public int DelayMs { get; init; }

        public string? Behavior { get; init; }

        public int? FailUntilAttempt { get; init; }
    }
}
