namespace OmniPrep.Domain.Services;

/// <summary>
/// Tính toán chuỗi ngày học liên tiếp (Study Streak) dựa trên lịch sử nộp bài thật.
/// </summary>
public static class StreakCalculator
{
    public static int CalculateStreak(IEnumerable<DateTimeOffset> activityDates, DateTimeOffset currentReferenceTime)
    {
        var dates = activityDates
            .Select(d => DateOnly.FromDateTime(d.UtcDateTime))
            .Distinct()
            .OrderByDescending(d => d)
            .ToList();

        if (dates.Count == 0) return 0;

        var today = DateOnly.FromDateTime(currentReferenceTime.UtcDateTime);
        var yesterday = today.AddDays(-1);

        // Chuỗi chỉ còn hiệu lực nếu ngày gần nhất là hôm nay hoặc hôm qua
        var mostRecent = dates[0];
        if (mostRecent != today && mostRecent != yesterday)
        {
            return 0;
        }

        int streak = 1;
        var expectedPrevious = mostRecent.AddDays(-1);

        for (int i = 1; i < dates.Count; i++)
        {
            if (dates[i] == expectedPrevious)
            {
                streak++;
                expectedPrevious = expectedPrevious.AddDays(-1);
            }
            else
            {
                break;
            }
        }

        return streak;
    }
}
