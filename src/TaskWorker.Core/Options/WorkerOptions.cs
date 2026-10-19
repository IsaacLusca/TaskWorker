namespace TaskWorker.Core.Options;

public sealed class WorkerOptions
{
    public const string SectionName = "Worker";

    public int BatchSize { get; set; } = 10;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan LeaseTimeout { get; set; } = TimeSpan.FromMinutes(2);

    public TimeSpan LeaseRenewalInterval { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan LeaseRecoveryInterval { get; set; } = TimeSpan.FromSeconds(30);

    public RetryOptions Retry { get; set; } = new();
}
