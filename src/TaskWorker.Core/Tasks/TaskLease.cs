namespace TaskWorker.Core.Tasks;

public sealed record TaskLease(
    Guid Id,
    string Type,
    string Payload,
    int Attempt,
    int MaxAttempts);
