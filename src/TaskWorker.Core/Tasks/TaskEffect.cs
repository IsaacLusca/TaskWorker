namespace TaskWorker.Core.Tasks;

public sealed class TaskEffect
{
    private TaskEffect()
    {
    }

    public TaskEffect(Guid taskId, string? result, DateTimeOffset createdAt)
    {
        TaskId = taskId;
        Result = result;
        CreatedAt = createdAt;
    }

    public Guid TaskId { get; private set; }

    public string? Result { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
