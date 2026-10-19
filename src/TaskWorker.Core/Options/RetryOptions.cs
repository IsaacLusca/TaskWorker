namespace TaskWorker.Core.Options;

public sealed class RetryOptions
{
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(2);

    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromMinutes(1);
}
