namespace TaskWorker.Core.Tasks;

public sealed record TaskContext(
    Guid TaskId,
    string Type,
    string Payload,
    int Attempt,
    int MaxAttempts);
