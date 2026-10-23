using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using OmniPrep.Domain.Enums;
using OmniPrep.Domain.ValueObjects;
using OmniPrep.Infrastructure.Data;
using static OmniPrep.Api.Endpoints.EndpointHelpers;

namespace OmniPrep.Api.Endpoints;

// ============================================================
// QUESTION MODERATION - Kiểm duyệt & hiệu đính câu hỏi (Teacher & Admin)
// ============================================================
public static class QuestionModerationEndpoints
{
    public static RouteGroupBuilder MapQuestionModerationApi(this RouteGroupBuilder group)
    {
        var moderation = group.MapGroup("/admin/questions").WithTags("Question Moderation").RequireAuthorization();

        moderation.MapGet("/pending", HandleListPendingQuestionsAsync).WithName("ListPendingQuestions");
        moderation.MapPost("/{questionId:guid}/verify", HandleVerifyQuestionAsync).WithName("VerifyQuestion");
        moderation.MapPost("/{questionId:guid}/reject", HandleRejectQuestionAsync).WithName("RejectQuestion");
        moderation.MapPut("/{questionId:guid}", HandleUpdatePendingQuestionAsync).WithName("UpdatePendingQuestion");
        moderation.MapGet("/stats", HandleGetQuestionModerationStatsAsync).WithName("GetQuestionModerationStats");
        moderation.MapPost("/verify-all", HandleVerifyAllQuestionsAsync).WithName("VerifyAllQuestions");

        return group;
    }

    public static async Task<IResult> HandleListPendingQuestionsAsync(
        int? limit,
        int? offset,
        ClaimsPrincipal user,
        OmniPrepDbContext db,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (!IsTeacherOrAdmin(user)) return Results.Forbid();
        try
        {
            var pageSize = Math.Clamp(limit ?? 50, 1, 100);
            var pageOffset = Math.Max(offset ?? 0, 0);
            var rawQuestions = await db.Questions.AsNoTracking()
                .Where(q => q.IsActive && !q.IsVerified)
                .OrderBy(q => q.CreatedAt).ThenBy(q => q.Id)
                .Skip(pageOffset)
                .Take(pageSize)
                .Select(q => new
                {
                    q.Id,
                    q.Content,
                    q.AttachedTable,
                    q.Format,
                    q.Difficulty,
                    q.Topic,
                    q.AnswerData,
                    q.Explanation,
                    q.DistractorAnalysis,
                    q.ImageUrls,
                    q.SourceTags,
                    q.CreatedAt,
                    SubjectCode = q.Subject != null ? q.Subject.Code : default,
                    SubjectName = q.Subject != null ? q.Subject.Name : string.Empty
                })
                .ToListAsync(ct);

            var questions = rawQuestions.Select(q => new
            {
                q.Id,
                q.Content,
                q.AttachedTable,
                Format = q.Format.ToString(),
                Difficulty = q.Difficulty.ToString(),
                q.Topic,
                q.AnswerData,
                q.Explanation,
                q.DistractorAnalysis,
                q.ImageUrls,
                q.SourceTags,
                q.CreatedAt,
                SubjectCode = q.SubjectCode.ToString(),
                q.SubjectName
            });

            return Results.Ok(questions);
        }
        catch (Exception ex)
        {
            var logger = httpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("QuestionModerationEndpoints");
            logger.LogError(ex, "Lỗi khi tải danh sách câu hỏi chờ duyệt");
            return Results.Problem(
                title: "Lỗi tải câu hỏi chờ duyệt",
                detail: "Đã xảy ra lỗi nội bộ khi tải danh sách câu hỏi chờ duyệt.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    public static async Task<IResult> HandleVerifyQuestionAsync(
        Guid questionId,
        ClaimsPrincipal user,
        OmniPrepDbContext db,
        CancellationToken ct)
    {
        if (!IsTeacherOrAdmin(user)) return Results.Forbid();
        var reviewerId = GetUserId(user);
        var reviewedAt = DateTimeOffset.UtcNow;
        var updated = await db.Questions
            .Where(q => q.Id == questionId && q.IsActive && !q.IsVerified)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(q => q.IsVerified, true)
                .SetProperty(q => q.ReviewedBy, reviewerId)
                .SetProperty(q => q.ReviewedAt, reviewedAt)
                .SetProperty(q => q.ReviewNote, (string?)null)
                .SetProperty(q => q.UpdatedAt, reviewedAt), ct);
        if (updated == 0)
        {
            var exists = await db.Questions.AnyAsync(q => q.Id == questionId, ct);
            return exists
                ? Results.Conflict(new { message = "Question is already reviewed or inactive." })
                : Results.NotFound();
        }
        return Results.Ok(new { Id = questionId, IsVerified = true, ReviewedBy = reviewerId, ReviewedAt = reviewedAt });
    }

    public static async Task<IResult> HandleRejectQuestionAsync(
        Guid questionId,
        RejectQuestionRequest? request,
        ClaimsPrincipal user,
        OmniPrepDbContext db,
        CancellationToken ct)
    {
        if (!IsTeacherOrAdmin(user)) return Results.Forbid();
        var note = request?.Note?.Trim();
        if (string.IsNullOrWhiteSpace(note) || note.Length > 1000)
            return Results.BadRequest(new { message = "A rejection reason of 1–1000 characters is required." });
        var reviewerId = GetUserId(user);
        var reviewedAt = DateTimeOffset.UtcNow;
        var updated = await db.Questions
            .Where(q => q.Id == questionId && q.IsActive && !q.IsVerified)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(q => q.IsActive, false)
                .SetProperty(q => q.ReviewedBy, reviewerId)
                .SetProperty(q => q.ReviewedAt, reviewedAt)
                .SetProperty(q => q.ReviewNote, note)
                .SetProperty(q => q.UpdatedAt, reviewedAt), ct);
        if (updated == 0)
        {
            var exists = await db.Questions.AnyAsync(q => q.Id == questionId, ct);
            return exists
                ? Results.Conflict(new { message = "Question is already reviewed or inactive." })
                : Results.NotFound();
        }
        return Results.Ok(new { Id = questionId, IsActive = false, ReviewedBy = reviewerId, ReviewedAt = reviewedAt, ReviewNote = note });
    }

    public static async Task<IResult> HandleUpdatePendingQuestionAsync(
        Guid questionId,
        UpdateQuestionRequest req,
        ClaimsPrincipal user,
        OmniPrepDbContext db,
        CancellationToken ct)
    {
        if (!IsTeacherOrAdmin(user)) return Results.Forbid();
        var q = await db.Questions.FirstOrDefaultAsync(q => q.Id == questionId, ct);
        if (q is null) return Results.NotFound();
        if (q.IsVerified) return Results.Conflict(new { message = "Không thể sửa câu hỏi đã được xuất bản/duyệt." });

        if (!string.IsNullOrWhiteSpace(req.Content))
            q.Content = req.Content.Trim();

        if (req.AttachedTable != null)
            q.AttachedTable = string.IsNullOrWhiteSpace(req.AttachedTable) ? null : req.AttachedTable.Trim();

        if (!string.IsNullOrWhiteSpace(req.Topic))
            q.Topic = req.Topic.Trim();

        if (!string.IsNullOrWhiteSpace(req.Difficulty) && Enum.TryParse<DifficultyLevel>(req.Difficulty, true, out var diff))
            q.Difficulty = diff;

        if (req.Explanation != null)
            q.Explanation = string.IsNullOrWhiteSpace(req.Explanation) ? null : req.Explanation.Trim();

        if (req.DistractorAnalysis != null)
            q.DistractorAnalysis = req.DistractorAnalysis;

        if (req.AnswerData != null)
            q.AnswerData = req.AnswerData;

        q.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return Results.Ok(new
        {
            q.Id,
            q.Content,
            q.AttachedTable,
            q.Topic,
            Difficulty = q.Difficulty.ToString(),
            q.Explanation,
            q.DistractorAnalysis,
            q.AnswerData,
            q.UpdatedAt
        });
    }

    public static async Task<IResult> HandleGetQuestionModerationStatsAsync(
        ClaimsPrincipal user,
        OmniPrepDbContext db,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (!IsTeacherOrAdmin(user)) return Results.Forbid();

        try
        {
            var totalPending = await db.Questions.CountAsync(q => q.IsActive && !q.IsVerified, ct);
            var totalVerified = await db.Questions.CountAsync(q => q.IsActive && q.IsVerified, ct);
            var totalRejected = await db.Questions.CountAsync(q => !q.IsActive, ct);

            var subjects = await db.Subjects.AsNoTracking()
                .Select(s => new
                {
                    SubjectCode = s.Code,
                    SubjectName = s.Name,
                    Pending = s.Questions.Count(q => q.IsActive && !q.IsVerified),
                    Verified = s.Questions.Count(q => q.IsActive && q.IsVerified),
                    Rejected = s.Questions.Count(q => !q.IsActive)
                })
                .ToListAsync(ct);

            var bySubject = subjects.Select(s => new
            {
                SubjectCode = s.SubjectCode.ToString(),
                s.SubjectName,
                s.Pending,
                s.Verified,
                s.Rejected
            });

            return Results.Ok(new
            {
                TotalPending = totalPending,
                TotalVerified = totalVerified,
                TotalRejected = totalRejected,
                BySubject = bySubject
            });
        }
        catch (Exception ex)
        {
            var logger = httpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("QuestionModerationEndpoints");
            logger.LogError(ex, "Lỗi khi lấy thống kê duyệt câu hỏi");
            return Results.Problem(
                title: "Lỗi thống kê",
                detail: "Đã xảy ra lỗi nội bộ khi lấy thống kê.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    public static async Task<IResult> HandleVerifyAllQuestionsAsync(
        VerifyAllQuestionsRequest? req,
        ClaimsPrincipal user,
        OmniPrepDbContext db,
        CancellationToken ct)
    {
        if (!IsTeacherOrAdmin(user)) return Results.Forbid();
        var reviewerId = GetUserId(user);
        var reviewedAt = DateTimeOffset.UtcNow;

        var query = db.Questions.Where(q => q.IsActive && !q.IsVerified);
        if (req?.QuestionIds != null && req.QuestionIds.Count > 0)
        {
            query = query.Where(q => req.QuestionIds.Contains(q.Id));
        }
        else if (!string.IsNullOrWhiteSpace(req?.SubjectCode) && Enum.TryParse<SubjectCode>(req.SubjectCode, true, out var code))
        {
            query = query.Where(q => q.Subject.Code == code);
        }

        var updatedCount = await query.ExecuteUpdateAsync(setters => setters
            .SetProperty(q => q.IsVerified, true)
            .SetProperty(q => q.ReviewedBy, reviewerId)
            .SetProperty(q => q.ReviewedAt, reviewedAt)
            .SetProperty(q => q.ReviewNote, (string?)null)
            .SetProperty(q => q.UpdatedAt, reviewedAt), ct);

        return Results.Ok(new
        {
            updatedCount,
            reviewedBy = reviewerId,
            reviewedAt,
            message = $"Đã duyệt thành công {updatedCount} câu hỏi vào kho đề chính thức."
        });
    }

    public sealed record VerifyAllQuestionsRequest(IReadOnlyList<Guid>? QuestionIds, string? SubjectCode);
    public sealed record RejectQuestionRequest(string? Note);
    public sealed record UpdateQuestionRequest(
        string? Content,
        string? AttachedTable,
        string? Topic,
        string? Difficulty,
        string? Explanation,
        AnswerData? AnswerData,
        Dictionary<string, string>? DistractorAnalysis);
}
