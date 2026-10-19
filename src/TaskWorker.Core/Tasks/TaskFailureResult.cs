namespace TaskWorker.Core.Tasks;

public sealed record TaskFailureResult(
    TaskState State,
    int Attempts,
    DateTimeOffset? NextAttemptAt);
