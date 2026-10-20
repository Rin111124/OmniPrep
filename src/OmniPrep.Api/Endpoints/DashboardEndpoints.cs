using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using OmniPrep.Domain.Enums;
using OmniPrep.Domain.Interfaces;
using OmniPrep.Domain.Services;
using OmniPrep.Infrastructure.Data;
using static OmniPrep.Api.Endpoints.EndpointHelpers;

namespace OmniPrep.Api.Endpoints;

// ============================================================
// DASHBOARD - Hồ sơ năng lực & Heatmap
// ============================================================
public static class DashboardEndpoints
{
    public static RouteGroupBuilder MapDashboardApi(this RouteGroupBuilder group)
    {
        var dashboard = group.MapGroup("/dashboard").WithTags("Dashboard").RequireAuthorization();

        /// <summary>Lấy dữ liệu Heatmap năng lực theo môn học</summary>
        dashboard.MapGet("/mastery/{subjectCode}", async (
            string subjectCode,
            IMasteryRepository masteryRepo,
            ISubjectRepository subjectRepo,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var userId = GetUserId(user);
            if (userId == Guid.Empty) return Results.Unauthorized();

            if (!Enum.TryParse<SubjectCode>(subjectCode, ignoreCase: true, out var code))
                return Results.BadRequest($"Mã môn học không hợp lệ: {subjectCode}");

            var subject = await subjectRepo.GetByCodeAsync(code, ct);
            if (subject is null) return Results.NotFound();

            var masteries = await masteryRepo.GetUserMasteryAsync(userId, subject.Id, ct);

            // Map tất cả topics của môn, kể cả chưa học (mastery = 0)
            var heatmapData = subject.Topics.Select(topic =>
            {
                var mastery = masteries.FirstOrDefault(m => m.Topic == topic);
                return new
                {
                    Topic = topic,
                    MasteryRate = mastery?.MasteryRate ?? 0m,
                    TotalAttempts = mastery?.TotalAttempts ?? 0,
                    TotalCorrect = mastery?.TotalCorrect ?? 0,
                    Level = mastery?.Level.ToString() ?? "NotStarted",
                    LastPracticed = mastery?.LastPracticed
                };
            });

            return Results.Ok(new
            {
                SubjectCode = code.ToString(),
                SubjectName = subject.Name,
                OverallMasteryRate = masteries.Any()
                    ? masteries.Average(m => (double)m.MasteryRate)
                    : 0.0,
                Topics = heatmapData
            });
        }).WithName("GetMasteryHeatmap");

        /// <summary>Lấy lịch sử thi gần nhất</summary>
        dashboard.MapGet("/history", async (
            IAttemptRepository attemptRepo,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var userId = GetUserId(user);
            if (userId == Guid.Empty) return Results.Unauthorized();

            var attempts = await attemptRepo.GetUserAttemptsAsync(userId, limit: 10, ct);
            return Results.Ok(attempts.Select(a => new
            {
                a.Id,
                a.SubjectId,
                SubjectCode = a.Exam.Subject.Code.ToString(),
                SubjectName = a.Exam.Subject.Name,
                a.TotalScore,
                a.ScorePart1,
                a.ScorePart2,
                a.ScorePart3,
                a.IsSubmitted,
                a.StartedAt,
                a.SubmittedAt,
                TimeSpentMinutes = a.TimeSpentSeconds.HasValue
                    ? Math.Round(a.TimeSpentSeconds.Value / 60.0, 1)
                    : (double?)null
            }));
        }).WithName("GetHistory");

        /// <summary>Lấy tóm tắt tiến độ, chuỗi ngày học thực tế và mục tiêu điểm</summary>
        dashboard.MapGet("/summary", async (
            ClaimsPrincipal user,
            OmniPrepDbContext db,
            CancellationToken ct) =>
        {
            var userId = GetUserId(user);
            if (userId == Guid.Empty) return Results.Unauthorized();

            var attempts = await db.ExamAttempts.AsNoTracking()
                .Where(a => a.UserId == userId && a.IsSubmitted)
                .OrderByDescending(a => a.StartedAt)
                .Select(a => new
                {
                    a.Id,
                    a.TotalScore,
                    Date = a.SubmittedAt ?? a.StartedAt,
                    TimeSpentSeconds = a.TimeSpentSeconds ?? 0
                })
                .ToListAsync(ct);

            var profile = await db.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == userId, ct);

            // Tính chuỗi ngày học thực tế
            var streakDays = StreakCalculator.CalculateStreak(
                attempts.Select(a => a.Date), DateTimeOffset.UtcNow);

            // Tính targetScore từ profile
            decimal targetScore = 0;
            if (profile?.ScoreGoals != null && profile.ScoreGoals.Count > 0)
            {
                targetScore = profile.ScoreGoals.Values.Average();
            }

            var scores = attempts.Select(a => (double)a.TotalScore).ToList();
            var totalTimeMinutes = Math.Round(attempts.Sum(a => a.TimeSpentSeconds) / 60.0, 1);

            return Results.Ok(new
            {
                StreakDays = streakDays,
                TargetScore = Math.Round(targetScore, 1),
                TotalExamsTaken = attempts.Count,
                TotalStudyTimeMinutes = totalTimeMinutes,
                AverageScore = scores.Count > 0 ? Math.Round(scores.Average(), 2) : 0,
                HighestScore = scores.Count > 0 ? Math.Round(scores.Max(), 2) : 0
            });
        }).WithName("GetDashboardSummary");

        return group;
    }
}
