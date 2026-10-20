using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using OmniPrep.Domain.Enums;
using OmniPrep.Infrastructure.Data;
using static OmniPrep.Api.Endpoints.EndpointHelpers;

namespace OmniPrep.Api.Endpoints;

// ============================================================
// USERS & RBAC - Quản lý người dùng và phân quyền vai trò
// ============================================================
public static class UsersEndpoints
{
    public static RouteGroupBuilder MapUsersApi(this RouteGroupBuilder group)
    {
        var users = group.MapGroup("/users").WithTags("Users & RBAC");

        /// <summary>Lấy thông tin người dùng hiện tại và vai trò RBAC</summary>
        users.MapGet("/me", (ClaimsPrincipal user) =>
        {
            var userId = GetUserId(user);
            if (userId == Guid.Empty) return Results.Unauthorized();
            var role = GetUserRole(user);

            return Results.Ok(new
            {
                Id = userId.ToString(),
                Role = role.ToString(),
                IsAdmin = role == UserRole.Admin,
                IsTeacher = role == UserRole.Teacher,
                IsStudent = role == UserRole.Student,
                Permissions = role switch
                {
                    UserRole.Admin => new[] { "read:all", "write:all", "manage:users", "manage:pipeline", "manage:questions" },
                    UserRole.Teacher => new[] { "read:all", "write:questions", "verify:questions", "read:analytics" },
                    _ => new[] { "read:exams", "submit:attempts", "read:personal_stats", "use:ai_tutor" }
                }
            });
        }).WithName("GetCurrentUser").RequireAuthorization();

        /// <summary>Xuất toàn bộ dữ liệu cá nhân (GDPR Data Portability)</summary>
        users.MapGet("/me/export", async (
            ClaimsPrincipal user,
            OmniPrepDbContext db,
            CancellationToken ct) =>
        {
            var userId = GetUserId(user);
            if (userId == Guid.Empty) return Results.Unauthorized();

            var profile = await db.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == userId, ct);
            var attempts = await db.ExamAttempts.AsNoTracking()
                .Where(a => a.UserId == userId)
                .OrderByDescending(a => a.StartedAt)
                .Select(a => new
                {
                    a.Id,
                    a.SubjectId,
                    SubjectCode = a.Exam.Subject.Code.ToString(),
                    a.TotalScore,
                    a.ScorePart1,
                    a.ScorePart2,
                    a.ScorePart3,
                    a.IsSubmitted,
                    a.StartedAt,
                    a.SubmittedAt,
                    a.TimeSpentSeconds
                })
                .ToListAsync(ct);

            var masteries = await db.UserTopicMasteries.AsNoTracking()
                .Where(m => m.UserId == userId)
                .Select(m => new
                {
                    m.Topic,
                    m.TotalAttempts,
                    m.TotalCorrect,
                    m.MasteryRate,
                    m.MasteryScore,
                    m.LastPracticed
                })
                .ToListAsync(ct);

            var mistakes = await db.UserMistakes.AsNoTracking()
                .Where(m => m.UserId == userId)
                .Select(m => new
                {
                    m.QuestionId,
                    SubjectCode = m.SubjectCode.ToString(),
                    m.StudentAnswer,
                    m.Mastered,
                    m.Notes,
                    m.CreatedAt
                })
                .ToListAsync(ct);

            return Results.Ok(new
            {
                ExportedAt = DateTimeOffset.UtcNow,
                UserId = userId,
                Profile = profile is null ? null : new
                {
                    profile.FullName,
                    profile.Grade,
                    profile.SchoolName,
                    profile.ScoreGoals,
                    profile.Role,
                    profile.CreatedAt
                },
                Attempts = attempts,
                TopicMasteries = masteries,
                Mistakes = mistakes
            });
        }).WithName("ExportUserData").RequireAuthorization();

        /// <summary>Xóa toàn bộ dữ liệu học tập cá nhân (Quyền được lãng quên / GDPR)</summary>
        users.MapDelete("/me", async (
            ClaimsPrincipal user,
            OmniPrepDbContext db,
            CancellationToken ct) =>
        {
            var userId = GetUserId(user);
            if (userId == Guid.Empty) return Results.Unauthorized();

            await db.UserMistakes.Where(m => m.UserId == userId).ExecuteDeleteAsync(ct);
            await db.AttemptAnswers.Where(a => a.UserId == userId).ExecuteDeleteAsync(ct);
            await db.UserQuestionHistories.Where(h => h.UserId == userId).ExecuteDeleteAsync(ct);
            await db.ExamAttempts.Where(a => a.UserId == userId).ExecuteDeleteAsync(ct);
            await db.GeneratedExams.Where(e => e.UserId == userId).ExecuteDeleteAsync(ct);
            await db.UserTopicMasteries.Where(m => m.UserId == userId).ExecuteDeleteAsync(ct);
            await db.AiTutorSessions.Where(s => s.UserId == userId).ExecuteDeleteAsync(ct);

            return Results.Ok(new { message = "Toàn bộ dữ liệu bài thi, sổ tay câu sai và lịch sử của người dùng đã được xóa." });
        }).WithName("DeleteUserData").RequireAuthorization();

        return group;
    }
}
