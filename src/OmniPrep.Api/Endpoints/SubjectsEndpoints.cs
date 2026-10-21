using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using OmniPrep.Domain.Enums;
using OmniPrep.Domain.Interfaces;
using OmniPrep.Infrastructure.Data;

namespace OmniPrep.Api.Endpoints;

// ============================================================
// SUBJECTS - Danh sách môn học
// ============================================================
public static class SubjectsEndpoints
{
    public static RouteGroupBuilder MapSubjectsApi(this RouteGroupBuilder group)
    {
        var subjects = group.MapGroup("/subjects").WithTags("Subjects");

        /// <summary>Lấy danh sách môn học đang hoạt động</summary>
        subjects.MapGet("/", async (
            ISubjectRepository repo,
            OmniPrepDbContext db,
            IEnumerable<ISubjectStrategy> strategies,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("SubjectsEndpoints");
            try
            {
                var data = await repo.GetAllActiveAsync(ct);
                var inventory = await db.Questions.AsNoTracking()
                    .Where(q => q.IsActive && q.IsVerified)
                    .GroupBy(q => new { q.SubjectId, q.Format })
                    .Select(g => new { g.Key.SubjectId, g.Key.Format, Count = g.Count() })
                    .ToListAsync(ct);
                var strategyCodes = strategies.Select(strategy => strategy.SubjectCode).ToHashSet();
                var response = data.Select(s =>
                {
                    var singleChoice = inventory.FirstOrDefault(i => i.SubjectId == s.Id && i.Format == QuestionFormat.MultipleChoice)?.Count ?? 0;
                    var trueFalse = inventory.FirstOrDefault(i => i.SubjectId == s.Id && i.Format == QuestionFormat.TrueFalse)?.Count ?? 0;
                    var shortAnswer = inventory.FirstOrDefault(i => i.SubjectId == s.Id && i.Format == QuestionFormat.ShortAnswer)?.Count ?? 0;
                    var hasStrategy = strategyCodes.Contains(s.Code);
                    var enoughQuestions = singleChoice >= s.ExamMatrix.Part1.Count
                        && trueFalse >= s.ExamMatrix.Part2.Count
                        && shortAnswer >= s.ExamMatrix.Part3.Count;
                    return new
                    {
                        s.Id,
                        s.Code,
                        s.Name,
                        s.Topics,
                        s.ExamMatrix,
                        s.TotalScore,
                        ActiveQuestionsCount = singleChoice + trueFalse + shortAnswer,
                        IsExamSupported = hasStrategy,
                        HasQuestionCapacity = enoughQuestions,
                        QuestionAvailability = new
                        {
                            Part1 = new { Available = singleChoice, Required = s.ExamMatrix.Part1.Count },
                            Part2 = new { Available = trueFalse, Required = s.ExamMatrix.Part2.Count },
                            Part3 = new { Available = shortAnswer, Required = s.ExamMatrix.Part3.Count }
                        }
                    };
                });
                return Results.Ok(response);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Subject catalog failed to load");
                return Results.Problem(
                    title: "Subject catalog unavailable",
                    detail: ex.ToString(),
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).WithName("GetSubjects").AllowAnonymous();

        return group;
    }
}
