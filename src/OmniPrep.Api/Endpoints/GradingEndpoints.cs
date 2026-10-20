using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OmniPrep.Domain.Entities;
using OmniPrep.Domain.Enums;
using OmniPrep.Domain.Interfaces;
using OmniPrep.Domain.ValueObjects;
using static OmniPrep.Api.Endpoints.EndpointHelpers;

namespace OmniPrep.Api.Endpoints;

// ============================================================
// GRADING - Nộp bài và nhận điểm
// ============================================================
public static class GradingEndpoints
{
    public static RouteGroupBuilder MapGradingApi(this RouteGroupBuilder group)
    {
        var grading = group.MapGroup("/attempts").WithTags("Grading").RequireAuthorization();

        grading.MapGet("/{attemptId:guid}", async (
            Guid attemptId,
            IAttemptRepository attemptRepo,
            IQuestionRepository questionRepo,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var userId = GetUserId(user);
            if (userId == Guid.Empty) return Results.Unauthorized();
            var attempt = await attemptRepo.GetByIdAsync(attemptId, ct);
            if (attempt is null) return Results.NotFound();
            if (attempt.UserId != userId) return Results.Forbid();
            if (attempt.IsSubmitted) return Results.Conflict(new { message = "Attempt is already submitted." });

            var questions = await questionRepo.GetByIdsAsync(attempt.Exam.QuestionIds, ct);
            var byId = questions.ToDictionary(q => q.Id);
            var answers = await attemptRepo.GetAttemptAnswersAsync(attemptId, ct);
            return Results.Ok(new
            {
                attempt.Id,
                attempt.ExamId,
                attempt.StartedAt,
                attempt.Exam.DurationMinutes,
                attempt.Exam.ExpiresAt,
                Questions = attempt.Exam.QuestionIds.Where(byId.ContainsKey).Select(id =>
                {
                    var q = byId[id];
                    return new
                    {
                        q.Id, q.SubjectId, q.Content, q.AttachedTable, q.Format, q.Difficulty, q.Topic, q.ImageUrls,
                        Options = q.Format == QuestionFormat.MultipleChoice ? q.AnswerData.Options : null,
                        Statements = q.Format == QuestionFormat.TrueFalse
                            ? q.AnswerData.Statements?.Select(s => new { s.Id, s.Text }) : null
                    };
                }),
                Answers = answers.Select(a => new { a.QuestionId, a.Answer, a.TimeSpentSeconds })
            });
        }).WithName("GetAttempt");

        grading.MapPut("/{attemptId:guid}/answers", async (
            Guid attemptId,
            SaveAttemptAnswersRequest req,
            IAttemptRepository attemptRepo,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var userId = GetUserId(user);
            if (userId == Guid.Empty) return Results.Unauthorized();
            var attempt = await attemptRepo.GetByIdAsync(attemptId, ct);
            if (attempt is null) return Results.NotFound();
            if (attempt.UserId != userId) return Results.Forbid();
            if (attempt.IsSubmitted) return Results.Conflict(new { message = "Attempt is already submitted." });
            if (attempt.Exam.ExpiresAt <= DateTimeOffset.UtcNow) return Results.Conflict(new { message = "Attempt has expired." });
            var now = DateTimeOffset.UtcNow;
            var deadline = attempt.StartedAt.AddMinutes(attempt.Exam.DurationMinutes);
            if (now > deadline.AddSeconds(30)) return Results.Conflict(new { message = "Attempt time has expired." });
            if (req.Answers.Select(a => a.QuestionId).Distinct().Count() != req.Answers.Count)
                return Results.BadRequest("Duplicate question answers.");
            if (req.Answers.Any(a => !attempt.Exam.QuestionIds.Contains(a.QuestionId)))
                return Results.BadRequest("Answer includes a question outside this exam.");

            var updatedAt = DateTimeOffset.UtcNow;
            var answers = req.Answers.Select(a => new AttemptAnswer
            {
                UserId = userId,
                AttemptId = attemptId,
                QuestionId = a.QuestionId,
                Answer = a.Answer,
                TimeSpentSeconds = a.TimeSpentSeconds,
                UpdatedAt = updatedAt
            }).ToList();
            attempt.TimeSpentSeconds = (int)Math.Clamp((now - attempt.StartedAt).TotalSeconds, 0, attempt.Exam.DurationMinutes * 60);
            await attemptRepo.SaveAttemptAnswersAsync(userId, attemptId, answers, ct);
            return Results.Ok(new { UpdatedAt = updatedAt });
        }).WithName("SaveAttemptAnswers");

        grading.MapGet("/{attemptId:guid}/result", async (
            Guid attemptId,
            IAttemptRepository attemptRepo,
            IQuestionRepository questionRepo,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var userId = GetUserId(user);
            if (userId == Guid.Empty) return Results.Unauthorized();
            var attempt = await attemptRepo.GetByIdAsync(attemptId, ct);
            if (attempt is null) return Results.NotFound();
            if (attempt.UserId != userId) return Results.Forbid();
            if (!attempt.IsSubmitted) return Results.Conflict(new { message = "Attempt has not been submitted." });

            var questions = await questionRepo.GetByIdsAsync(attempt.Exam.QuestionIds, ct);
            var questionById = questions.ToDictionary(q => q.Id);
            var historyById = (await attemptRepo.GetAttemptQuestionHistoryAsync(attemptId, ct))
                .GroupBy(h => h.QuestionId)
                .ToDictionary(group => group.Key, group => group.OrderByDescending(h => h.AnsweredAt).First());
            var details = attempt.Exam.QuestionIds.Select((questionId, index) =>
            {
                questionById.TryGetValue(questionId, out var question);
                historyById.TryGetValue(questionId, out var history);
                if (history is null) return null;
                return new
                {
                    QuestionId = questionId,
                    QuestionNumber = index + 1,
                    Status = history.Status,
                    PointsEarned = history.PointsEarned,
                    Topic = question?.Topic,
                    Difficulty = question?.Difficulty,
                    Format = question?.Format,
                    Content = question?.Content,
                    Explanation = question?.Explanation,
                    DistractorAnalysis = question?.DistractorAnalysis,
                    Answer = history.UserAnswer,
                    CorrectAnswerData = question?.AnswerData
                };
            }).Where(detail => detail is not null);

            return Results.Ok(new
            {
                attempt.Id,
                AttemptId = attempt.Id,
                attempt.ScorePart1,
                attempt.ScorePart2,
                attempt.ScorePart3,
                attempt.TotalScore,
                MaxScore = attempt.Exam.MatrixSnapshot.MaxScore,
                attempt.TimeSpentSeconds,
                Details = details
            });
        }).WithName("GetAttemptResult");

        /// <summary>Bắt đầu một lần thi (tạo attempt)</summary>
        grading.MapPost("/start", async (
            StartAttemptRequest req,
            IExamRepository examRepo,
            IAttemptRepository attemptRepo,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var userId = GetUserId(user);
            if (userId == Guid.Empty) return Results.Unauthorized();

            var exam = await examRepo.GetByIdAsync(req.ExamId, ct);
            if (exam is null) return Results.NotFound("Không tìm thấy đề thi");
            if (exam.UserId != userId) return Results.Forbid();
            if (exam.ExpiresAt < DateTimeOffset.UtcNow)
                return Results.BadRequest("Đề thi đã hết hạn");

            var attempt = new ExamAttempt
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ExamId = exam.Id,
                SubjectId = exam.SubjectId,
                IsSubmitted = false,
                StartedAt = DateTimeOffset.UtcNow
            };

            await attemptRepo.AddAsync(attempt, ct);
            await attemptRepo.SaveChangesAsync(ct);

            return Results.Ok(new { AttemptId = attempt.Id, StartedAt = attempt.StartedAt });
        }).WithName("StartAttempt");

        /// <summary>
        /// Nộp bài và nhận kết quả chấm điểm.
        /// Tự động cập nhật Mastery Heatmap sau khi chấm.
        /// </summary>
        grading.MapPost("/{attemptId:guid}/submit", async (
            Guid attemptId,
            SubmitAttemptRequest req,
            IAttemptRepository attemptRepo,
            IQuestionRepository questionRepo,
            IGradingService gradingService,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var userId = GetUserId(user);
            if (userId == Guid.Empty) return Results.Unauthorized();
            var attempt = await attemptRepo.GetByIdAsync(attemptId, ct);
            if (attempt is null) return Results.NotFound("Không tìm thấy lần thi");
            if (attempt.UserId != userId) return Results.Forbid();
            if (attempt.IsSubmitted) return Results.Conflict("Bài thi đã được nộp.");
            if (attempt.Exam.ExpiresAt <= DateTimeOffset.UtcNow) return Results.Conflict("Bài thi đã hết hạn.");
            var now = DateTimeOffset.UtcNow;
            var deadline = attempt.StartedAt.AddMinutes(attempt.Exam.DurationMinutes);
            if (now > deadline.AddSeconds(30) || attempt.Exam.ExpiresAt <= now)
                return Results.Conflict("Attempt has expired.");
            if (req.TimeSpentSeconds is < 0 or > 3000)
                return Results.BadRequest("Thời gian làm bài không hợp lệ.");
            attempt.TimeSpentSeconds = (int)Math.Clamp((now - attempt.StartedAt).TotalSeconds, 0, attempt.Exam.DurationMinutes * 60);
            if (req.Answers.Select(a => a.QuestionId).Distinct().Count() != req.Answers.Count)
                return Results.BadRequest("Danh sách câu trả lời có câu hỏi bị lặp.");
            if (req.Answers.Any(a => !attempt.Exam.QuestionIds.Contains(a.QuestionId)))
                return Results.BadRequest("Bài nộp chứa câu hỏi không thuộc đề thi này.");

            var submissions = new List<UserSubmission>(req.Answers.Count);
            foreach (var answer in req.Answers)
            {
                if (!Enum.TryParse<QuestionFormat>(answer.Format, true, out var format))
                    return Results.BadRequest($"Định dạng câu trả lời không hợp lệ: {answer.Format}");
                submissions.Add(new UserSubmission(answer.QuestionId, format, answer.Answer, answer.TimeSpentSeconds));
            }

            var result = await gradingService.GradeAttemptAsync(attemptId, submissions, ct);
            var gradedQuestions = await questionRepo.GetByIdsAsync(attempt.Exam.QuestionIds, ct);
            var gradedQuestionDict = gradedQuestions.ToDictionary(q => q.Id);
            var answerDict = req.Answers.ToDictionary(a => a.QuestionId);

            return Results.Ok(new
            {
                result.AttemptId,
                result.ScorePart1,
                result.ScorePart2,
                result.ScorePart3,
                result.TotalScore,
                MaxScore = attempt.Exam.MatrixSnapshot.MaxScore,
                TimeSpentSeconds = attempt.TimeSpentSeconds,
                Details = result.Details.Select((d, index) =>
                {
                    gradedQuestionDict.TryGetValue(d.QuestionId, out var q);
                    answerDict.TryGetValue(d.QuestionId, out var answer);
                    return new
                    {
                        d.QuestionId,
                        QuestionNumber = index + 1,
                        d.Status,
                        d.PointsEarned,
                        d.Topic,
                        d.Difficulty,
                        Format = q?.Format,
                        Content = q?.Content,
                        Explanation = q?.Explanation,
                        DistractorAnalysis = q?.DistractorAnalysis,
                        Answer = answer?.Answer,
                        CorrectAnswerData = q?.AnswerData
                    };
                })
            });
        }).WithName("SubmitAttempt");

        return group;
    }

    public sealed record StartAttemptRequest(Guid ExamId);
    public sealed record SubmitAttemptRequest(IReadOnlyList<AnswerSubmission> Answers, int? TimeSpentSeconds);
    public sealed record SaveAttemptAnswersRequest(IReadOnlyList<AttemptAnswerSubmission> Answers, int? TimeSpentSeconds);
    public sealed record AttemptAnswerSubmission(Guid QuestionId, Dictionary<string, object>? Answer, int? TimeSpentSeconds);
    public sealed record AnswerSubmission(
        Guid QuestionId,
        string Format,
        Dictionary<string, object>? Answer,
        int? TimeSpentSeconds);
}
