using OmniPrep.Domain.Services;
using Xunit;

namespace OmniPrep.Tests;

public class StreakCalculatorTests
{
    private readonly DateTimeOffset _referenceTime = new(2026, 10, 20, 10, 0, 0, TimeSpan.Zero); // 2026-10-20

    [Fact]
    public void CalculateStreak_EmptyDates_ReturnsZero()
    {
        var streak = StreakCalculator.CalculateStreak(Array.Empty<DateTimeOffset>(), _referenceTime);
        Assert.Equal(0, streak);
    }

    [Fact]
    public void CalculateStreak_ActiveTodayOnly_ReturnsOne()
    {
        var dates = new[] { new DateTimeOffset(2026, 10, 20, 8, 30, 0, TimeSpan.Zero) };
        var streak = StreakCalculator.CalculateStreak(dates, _referenceTime);
        Assert.Equal(1, streak);
    }

    [Fact]
    public void CalculateStreak_ActiveYesterdayOnly_ReturnsOne()
    {
        var dates = new[] { new DateTimeOffset(2026, 10, 19, 21, 0, 0, TimeSpan.Zero) };
        var streak = StreakCalculator.CalculateStreak(dates, _referenceTime);
        Assert.Equal(1, streak);
    }

    [Fact]
    public void CalculateStreak_MissedYesterdayAndToday_ReturnsZero()
    {
        var dates = new[]
        {
            new DateTimeOffset(2026, 10, 18, 14, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 17, 14, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 16, 14, 0, 0, TimeSpan.Zero)
        };
        var streak = StreakCalculator.CalculateStreak(dates, _referenceTime);
        Assert.Equal(0, streak);
    }

    [Fact]
    public void CalculateStreak_MultipleActivitiesSameDay_Deduplicates()
    {
        var dates = new[]
        {
            new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 20, 20, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 19, 10, 0, 0, TimeSpan.Zero)
        };
        var streak = StreakCalculator.CalculateStreak(dates, _referenceTime);
        Assert.Equal(2, streak);
    }

    [Fact]
    public void CalculateStreak_FiveConsecutiveDays_ReturnsFive()
    {
        var dates = new[]
        {
            new DateTimeOffset(2026, 10, 20, 1, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 19, 1, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 18, 1, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 17, 1, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 16, 1, 0, 0, TimeSpan.Zero)
        };
        var streak = StreakCalculator.CalculateStreak(dates, _referenceTime);
        Assert.Equal(5, streak);
    }

    [Fact]
    public void CalculateStreak_GapInHistory_StopsAtGap()
    {
        var dates = new[]
        {
            new DateTimeOffset(2026, 10, 20, 1, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 19, 1, 0, 0, TimeSpan.Zero),
            // Gap on 10-18
            new DateTimeOffset(2026, 10, 17, 1, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 16, 1, 0, 0, TimeSpan.Zero)
        };
        var streak = StreakCalculator.CalculateStreak(dates, _referenceTime);
        Assert.Equal(2, streak);
    }
}
