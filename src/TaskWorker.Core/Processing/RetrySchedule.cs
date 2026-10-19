using TaskWorker.Core.Options;

namespace TaskWorker.Core.Processing;

public sealed class RetrySchedule
{
    private readonly RetryOptions _options;

    public RetrySchedule(RetryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.BaseDelay <= TimeSpan.Zero)
        {
            throw new ArgumentException("Retry base delay must be greater than zero.", nameof(options));
        }

        if (options.MaxDelay < options.BaseDelay)
        {
            throw new ArgumentException("Retry max delay must be greater than or equal to the base delay.", nameof(options));
        }

        _options = options;
    }

    public TimeSpan GetDelay(int failedAttempt)
    {
        if (failedAttempt < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(failedAttempt), "Failed attempt must be at least 1.");
        }

        var exponent = Math.Min(failedAttempt - 1, 30);
        var ticks = _options.BaseDelay.Ticks * Math.Pow(2, exponent);

        return TimeSpan.FromTicks((long)Math.Min(ticks, _options.MaxDelay.Ticks));
    }
}
