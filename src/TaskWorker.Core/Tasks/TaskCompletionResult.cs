namespace TaskWorker.Core.Tasks;

public sealed record TaskCompletionResult(
    bool TaskUpdated,
    bool EffectCreated);
