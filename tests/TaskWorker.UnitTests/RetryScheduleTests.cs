using TaskWorker.Core.Options;
using TaskWorker.Core.Processing;

namespace TaskWorker.UnitTests;

public sealed class RetryScheduleTests
{
    [Fact]
    public void First_failure_uses_the_base_delay()
    {
        var schedule = CreateSchedule(TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(1));

        Assert.Equal(TimeSpan.FromSeconds(2), schedule.GetDelay(1));
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    [InlineData(4, 16)]
    public void Delay_grows_exponentially(int failedAttempt, int expectedSeconds)
    {
        var schedule = CreateSchedule(TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(1));

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), schedule.GetDelay(failedAttempt));
    }

    [Fact]
    public void Delay_is_capped_by_the_max_delay()
    {
        var schedule = CreateSchedule(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5));

        Assert.Equal(TimeSpan.FromSeconds(5), schedule.GetDelay(5));
    }

    [Fact]
    public void Failed_attempt_must_be_positive()
    {
        var schedule = CreateSchedule(TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(1));

        Assert.Throws<ArgumentOutOfRangeException>(() => schedule.GetDelay(0));
    }

    [Fact]
    public void Base_delay_must_be_positive()
    {
        var options = new RetryOptions
        {
            BaseDelay = TimeSpan.Zero,
            MaxDelay = TimeSpan.FromMinutes(1)
        };

        Assert.Throws<ArgumentException>(() => new RetrySchedule(options));
    }

    [Fact]
    public void Max_delay_must_not_be_smaller_than_base_delay()
    {
        var options = new RetryOptions
        {
            BaseDelay = TimeSpan.FromSeconds(10),
            MaxDelay = TimeSpan.FromSeconds(5)
        };

        Assert.Throws<ArgumentException>(() => new RetrySchedule(options));
    }

    private static RetrySchedule CreateSchedule(TimeSpan baseDelay, TimeSpan maxDelay) =>
        new(new RetryOptions
        {
            BaseDelay = baseDelay,
            MaxDelay = maxDelay
        });
}
