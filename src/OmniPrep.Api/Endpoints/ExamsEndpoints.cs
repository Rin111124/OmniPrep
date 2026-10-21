using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OmniPrep.Domain.Enums;
using OmniPrep.Domain.Interfaces;
using static OmniPrep.Api.Endpoints.EndpointHelpers;

namespace OmniPrep.Api.Endpoints;

// ============================================================
// EXAMS - Tạo đề thi và lấy đề
// ============================================================
public static class ExamsEndpoints
{
    public static RouteGroupBuilder MapExamsApi(this RouteGroupBuilder group)
    {
        var exams = group.MapGroup("/exams").WithTags("Exams").RequireAuthorization();

        /// <summary>Tạo đề thi mới theo ma trận cho học sinh</summary>
        exams.MapPost("/generate", async (
            GenerateExamRequest req,
            IExamGenerationService service,
            IQuestionRepository questionRepo,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var userId = GetUserId(user);
            if (userId == Guid.Empty) return Results.Unauthorized();

            if (!Enum.TryParse<SubjectCode>(req.SubjectCode, ignoreCase: true, out var subjectCode))
                return Results.BadRequest($"Mã môn học không hợp lệ: {req.SubjectCode}");

            Domain.Entities.GeneratedExam exam;
            try
            {
                exam = await service.GenerateExamAsync(userId, subjectCode, req.SourceName, ct);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { message = ex.Message });
            }
            catch (NotSupportedException ex)
            {
                return Results.Problem(title: "Subject is not supported yet", detail: ex.Message, statusCode: StatusCodes.Status501NotImplemented);
            }

            // Load chi tiết câu hỏi để trả về frontend
            var questions = await questionRepo.GetByIdsAsync(exam.QuestionIds, ct);
            var questionDict = questions.ToDictionary(q => q.Id);

            return Results.Ok(new
            {
                ExamId = exam.Id,
                SubjectCode = subjectCode.ToString(),
                DurationMinutes = exam.DurationMinutes,
                ExpiresAt = exam.ExpiresAt,
                Questions = exam.QuestionIds.Select(id =>
                {
                    if (!questionDict.TryGetValue(id, out var q)) return null;
                    return new
                    {
                        q.Id,
                        q.Content,
                        q.AttachedTable,
                        q.Format,
                        q.Difficulty,
                        q.Topic,
                        q.ImageUrls,
                        q.SourceTags,
                        // Trả về options cho MC, statements cho TF
                        // KHÔNG trả về đáp án đúng (correct, is_correct)
                        Options = q.Format == QuestionFormat.MultipleChoice
                            ? q.AnswerData.Options
                            : null,
                        Statements = q.Format == QuestionFormat.TrueFalse
                            ? q.AnswerData.Statements?.Select(s => new { s.Id, s.Text })
                            : null
                    };
                }).Where(q => q != null)
            });
        }).WithName("GenerateExam");

        return group;
    }

    public sealed record GenerateExamRequest(string SubjectCode, string? SourceName = null);
}
