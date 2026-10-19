namespace TaskWorker.Core.Tasks;

public sealed class TaskItem
{
    public const int MaxErrorLength = 4000;

    private TaskItem()
    {
        Type = null!;
        Payload = null!;
    }

    public TaskItem(
        string type,
        string payload,
        int maxAttempts,
        string? idempotencyKey,
        DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(payload);

        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "Max attempts must be at least 1.");
        }

        if (idempotencyKey is { Length: > 200 })
        {
            throw new ArgumentException("Idempotency key must have at most 200 characters.", nameof(idempotencyKey));
        }

        Id = Guid.NewGuid();
        Type = type;
        Payload = payload;
        State = TaskState.Pending;
        MaxAttempts = maxAttempts;
        IdempotencyKey = idempotencyKey;
        CreatedAt = createdAt;
        NextAttemptAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public string Type { get; private set; }

    public string Payload { get; private set; }

    public TaskState State { get; private set; }

    public int Attempts { get; private set; }

    public int MaxAttempts { get; private set; }

    public string? LastError { get; private set; }

    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public DateTimeOffset? NextAttemptAt { get; private set; }

    public DateTimeOffset? LockedAt { get; private set; }

    public string? LockedBy { get; private set; }

    public DateTimeOffset? FinishedAt { get; private set; }
}
