using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OmniPrep.Domain.Entities;
using OmniPrep.Domain.Enums;
using OmniPrep.Infrastructure.Data;
using static OmniPrep.Api.Endpoints.EndpointHelpers;

namespace OmniPrep.Api.Endpoints;

// ============================================================
// MISTAKES - Sổ tay bẫy đề thi cá nhân hóa
// ============================================================
public static class MistakesEndpoints
{
    public static RouteGroupBuilder MapMistakesApi(this RouteGroupBuilder group)
    {
        var mistakes = group.MapGroup("/mistakes").WithTags("Mistake Notebook").RequireAuthorization();

        /// <summary>Lấy danh sách các câu hỏi trong sổ tay bẫy đề thi</summary>
        mistakes.MapGet("/", async (
            string? subjectCode,
            string? format,
            bool? mastered,
            ClaimsPrincipal user,
            OmniPrepDbContext db,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("MistakesEndpoints");
            try
            {
                var userId = GetUserId(user);
                if (userId == Guid.Empty) return Results.Unauthorized();

                var query = db.UserMistakes.AsNoTracking()
                    .Include(m => m.Question)
                    .Where(m => m.UserId == userId);

                if (!string.IsNullOrWhiteSpace(subjectCode) && Enum.TryParse<SubjectCode>(subjectCode, true, out var code))
                {
                    query = query.Where(m => m.SubjectCode == code);
                }

                if (!string.IsNullOrWhiteSpace(format) && Enum.TryParse<QuestionFormat>(format, true, out var fmt))
                {
                    query = query.Where(m => m.Question.Format == fmt);
                }

                if (mastered.HasValue)
                {
                    query = query.Where(m => m.Mastered == mastered.Value);
                }

                var rawList = await query
                    .OrderByDescending(m => m.CreatedAt)
                    .ToListAsync(ct);

                var list = rawList.Select(m => new
                {
                    m.Id,
                    m.QuestionId,
                    SubjectCode = m.SubjectCode.ToString(),
                    m.StudentAnswer,
                    m.Mastered,
                    m.Notes,
                    m.CreatedAt,
                    m.UpdatedAt,
                    Question = m.Question == null ? null : new
                    {
                        m.Question.Id,
                        m.Question.SubjectId,
                        m.Question.Content,
                        m.Question.AttachedTable,
                        m.Question.ImageUrls,
                        m.Question.Format,
                        m.Question.Difficulty,
                        m.Question.Topic,
                        Options = m.Question.Format == QuestionFormat.MultipleChoice ? m.Question.AnswerData?.Options : null,
                        Statements = m.Question.Format == QuestionFormat.TrueFalse ? m.Question.AnswerData?.Statements : null,
                        m.Question.Explanation,
                        m.Question.DistractorAnalysis,
                        m.Question.AnswerData
                    }
                }).ToList();

                return Results.Ok(list);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to load mistakes for user");
                return Results.Problem(
                    title: "Mistakes load failed",
                    detail: "An internal error occurred while loading mistakes.",
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        }).WithName("GetMistakes");

        /// <summary>Đồng bộ câu sai từ kết quả bài thi hoặc từ client</summary>
        mistakes.MapPost("/sync", async (
            SyncMistakesRequest request,
            ClaimsPrincipal user,
            OmniPrepDbContext db,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("MistakesEndpoints");
            try
            {
                var userId = GetUserId(user);
                if (userId == Guid.Empty) return Results.Unauthorized();

                if (request.Items == null || request.Items.Count == 0)
                    return Results.Ok(new { syncedCount = 0 });

                if (request.Items.Count > 100)
                    return Results.BadRequest("Tối đa 100 câu sai mỗi lần đồng bộ.");

                var questionIds = request.Items.Select(i => i.QuestionId).Distinct().ToList();
                var validQuestions = await db.Questions.AsNoTracking()
                    .Where(q => questionIds.Contains(q.Id))
                    .Select(q => new { q.Id, q.Subject.Code })
                    .ToDictionaryAsync(q => q.Id, ct);

                var existingMistakes = await db.UserMistakes
                    .Where(m => m.UserId == userId && questionIds.Contains(m.QuestionId))
                    .ToDictionaryAsync(m => m.QuestionId, ct);

                var now = DateTimeOffset.UtcNow;
                int syncedCount = 0;

                foreach (var item in request.Items)
                {
                    if (!validQuestions.TryGetValue(item.QuestionId, out var q)) continue;

                    if (existingMistakes.TryGetValue(item.QuestionId, out var existing))
                    {
                        existing.StudentAnswer = item.StudentAnswer;
                        if (item.Mastered.HasValue) existing.Mastered = item.Mastered.Value;
                        if (item.Notes != null) existing.Notes = item.Notes;
                        existing.UpdatedAt = now;
                    }
                    else
                    {
                        db.UserMistakes.Add(new UserMistake
                        {
                            Id = Guid.NewGuid(),
                            UserId = userId,
                            QuestionId = item.QuestionId,
                            SubjectCode = q.Code,
                            StudentAnswer = item.StudentAnswer,
                            Mastered = item.Mastered ?? false,
                            Notes = item.Notes,
                            CreatedAt = now,
                            UpdatedAt = now
                        });
                    }
                    syncedCount++;
                }

                await db.SaveChangesAsync(ct);
                return Results.Ok(new { syncedCount });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to sync mistakes");
                return Results.Problem(
                    title: "Sync mistakes failed",
                    detail: "An internal error occurred while syncing mistakes.",
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        }).WithName("SyncMistakes");

        /// <summary>Chuyển đổi trạng thái đã thành thạo hay chưa</summary>
        mistakes.MapPatch("/{id:guid}/toggle-mastered", async (
            Guid id,
            ClaimsPrincipal user,
            OmniPrepDbContext db,
            CancellationToken ct) =>
        {
            var userId = GetUserId(user);
            if (userId == Guid.Empty) return Results.Unauthorized();

            var mistake = await db.UserMistakes.FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId, ct);
            if (mistake is null) return Results.NotFound();

            mistake.Mastered = !mistake.Mastered;
            mistake.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { mistake.Id, mistake.Mastered, mistake.UpdatedAt });
        }).WithName("ToggleMistakeMastered");

        /// <summary>Xóa một câu hỏi khỏi sổ tay bẫy đề thi</summary>
        mistakes.MapDelete("/{id:guid}", async (
            Guid id,
            ClaimsPrincipal user,
            OmniPrepDbContext db,
            CancellationToken ct) =>
        {
            var userId = GetUserId(user);
            if (userId == Guid.Empty) return Results.Unauthorized();

            var count = await db.UserMistakes
                .Where(m => m.Id == id && m.UserId == userId)
                .ExecuteDeleteAsync(ct);

            return count > 0 ? Results.NoContent() : Results.NotFound();
        }).WithName("DeleteMistake");

        /// <summary>Xóa toàn bộ câu hỏi trong sổ tay bẫy đề thi của người dùng</summary>
        mistakes.MapDelete("/", async (
            ClaimsPrincipal user,
            OmniPrepDbContext db,
            CancellationToken ct) =>
        {
            var userId = GetUserId(user);
            if (userId == Guid.Empty) return Results.Unauthorized();

            var deleted = await db.UserMistakes
                .Where(m => m.UserId == userId)
                .ExecuteDeleteAsync(ct);

            return Results.Ok(new { deletedCount = deleted });
        }).WithName("ClearAllMistakes");

        return group;
    }

    public sealed record SyncMistakesRequest(IReadOnlyList<SyncMistakeItem> Items);
    public sealed record SyncMistakeItem(Guid QuestionId, Dictionary<string, object>? StudentAnswer, bool? Mastered, string? Notes);
}
