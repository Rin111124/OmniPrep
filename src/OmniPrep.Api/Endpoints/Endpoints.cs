using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using OmniPrep.Domain.Enums;
using OmniPrep.Domain.Entities;
using OmniPrep.Domain.Interfaces;
using OmniPrep.Domain.ValueObjects;
using OmniPrep.Infrastructure.Data;
using OmniPrep.Infrastructure.Services;
using static OmniPrep.Api.Endpoints.EndpointHelpers;

namespace OmniPrep.Api.Endpoints;

// ============================================================
// OmniPrep.Api - Minimal API Endpoint Groups
// Tổ chức endpoints theo domain, sử dụng RouteGroupBuilder
// ============================================================

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
            CancellationToken ct) =>
        {
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
            catch (Exception)
            {
                return Results.Problem(
                    title: "Subject catalog unavailable",
                    detail: "The subject catalog could not be loaded.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).WithName("GetSubjects").AllowAnonymous();

        return group;
    }
}

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
                exam = await service.GenerateExamAsync(userId, subjectCode, ct);
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

    private record GenerateExamRequest(string SubjectCode);
}

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

            var attempt = new Domain.Entities.ExamAttempt
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

    private record StartAttemptRequest(Guid ExamId);

    private record SubmitAttemptRequest(IReadOnlyList<AnswerSubmission> Answers, int? TimeSpentSeconds);
    private record SaveAttemptAnswersRequest(IReadOnlyList<AttemptAnswerSubmission> Answers, int? TimeSpentSeconds);

    private record AttemptAnswerSubmission(Guid QuestionId, Dictionary<string, object>? Answer, int? TimeSpentSeconds);

    private record AnswerSubmission(
        Guid QuestionId,
        string Format,
        Dictionary<string, object>? Answer,
        int? TimeSpentSeconds);
}

// ============================================================
// AI TUTOR - Giải thích lỗi sai qua Streaming
// ============================================================
public static class TutorEndpoints
{
    public static RouteGroupBuilder MapTutorApi(this RouteGroupBuilder group)
    {
        var tutor = group.MapGroup("/tutor").WithTags("AI Tutor");

        /// <summary>
        /// Endpoint chat streaming trực tiếp với Gia sư AI (Server-Sent Events)
        /// Phục vụ cho Drawer trên frontend khi học sinh hỏi về câu sai
        /// </summary>
        tutor.MapPost("/chat-stream", async (
            TutorContext context,
            IGeminiService geminiService,
            IOptions<GeminiOptions> geminiOptions,
            ILoggerFactory loggerFactory,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("OmniPrep.Api.Tutor");
            var apiKey = geminiOptions.Value.ApiKey;
            if (string.IsNullOrWhiteSpace(apiKey) || apiKey == "YOUR_GEMINI_API_KEY")
                return Results.Problem(title: "AI tutor unavailable", statusCode: StatusCodes.Status503ServiceUnavailable);

            httpContext.Response.Headers.ContentType = "text/event-stream";
            httpContext.Response.Headers.CacheControl = "no-cache";
            httpContext.Response.Headers.Connection = "keep-alive";
            try
            {
                await foreach (var chunk in geminiService.GetTutorExplanationStreamAsync(context, ct))
                {
                    var normalizedChunk = chunk.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
                    foreach (var line in normalizedChunk.Split('\n'))
                        await httpContext.Response.WriteAsync($"data: {line}\n", ct);
                    await httpContext.Response.WriteAsync("\n", ct);
                    await httpContext.Response.Body.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return Results.Empty;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "AI tutor stream failed");
                await httpContext.Response.WriteAsync("event: error\ndata: AI tutor is temporarily unavailable.\n\n", ct);
            }

            await httpContext.Response.WriteAsync("data: [DONE]\n\n", ct);
            return Results.Empty;
        }).WithName("ChatTutorStream").RequireAuthorization().RequireRateLimiting("tutor-per-user");

        /// <summary>
        /// Lấy giải thích từ AI Tutor cho một câu sai theo history ID.
        /// </summary>


        return group;
    }
}

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
            var streakDays = OmniPrep.Domain.Services.StreakCalculator.CalculateStreak(
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

// ============================================================
// INGESTION - Nạp đề thi (Admin only)
// ============================================================
public static class IngestionEndpoints
{
    public static RouteGroupBuilder MapIngestionApi(this RouteGroupBuilder group)
    {
        var ingestion = group.MapGroup("/admin/ingest").WithTags("Admin - Ingestion").RequireAuthorization();

        /// <summary>
        /// Bóc tách đề thi (hỗ trợ cả dán text/OCR trực tiếp hoặc upload file).
        /// </summary>
        ingestion.MapPost("/", async (
            IFormFile? file,
            [Microsoft.AspNetCore.Mvc.FromForm] string? rawContent,
            [Microsoft.AspNetCore.Mvc.FromForm] string subjectCode,
            [Microsoft.AspNetCore.Mvc.FromForm] string sourceName,
            [Microsoft.AspNetCore.Mvc.FromForm] short? examYear,
            ClaimsPrincipal user,
            OmniPrepDbContext db,
            IExamPaperRepository paperRepo,
            ISubjectRepository subjectRepo,
            CancellationToken ct) =>
        {
            if (!IsAdmin(user)) return Results.Forbid();
            if (!Enum.TryParse<SubjectCode>(subjectCode, ignoreCase: true, out var code))
                return Results.BadRequest($"Mã môn học không hợp lệ: {subjectCode}");

            var subject = await subjectRepo.GetByCodeAsync(code, ct);
            if (subject is null) return Results.NotFound($"Môn học không tồn tại: {subjectCode}");

            string contentToProcess = string.Empty;
            string title = "Đề thi nạp qua văn bản";

            if (file is not null && file.Length > 0)
            {
                if (!string.Equals(Path.GetExtension(file.FileName), ".txt", StringComparison.OrdinalIgnoreCase))
                    return Results.BadRequest("Hiện chỉ hỗ trợ file .txt hoặc dán nội dung văn bản.");
                if (file.Length > 2 * 1024 * 1024)
                    return Results.BadRequest("Kích thước file tối đa là 2 MB.");
                title = file.FileName;
                using var reader = new StreamReader(file.OpenReadStream());
                contentToProcess = await reader.ReadToEndAsync(ct);
            }
            else if (!string.IsNullOrWhiteSpace(rawContent))
            {
                contentToProcess = rawContent;
            }
            else
            {
                return Results.BadRequest("Vui lòng tải lên file đề thi hoặc dán nội dung văn bản rawContent.");
            }

            var fileHash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(contentToProcess))).ToLowerInvariant();
            if (contentToProcess.Length > 2 * 1024 * 1024)
                return Results.BadRequest("Nội dung đề tối đa là 2 MB.");
            if (await paperRepo.GetByFileHashAsync(fileHash, ct) is not null)
                return Results.Conflict("Đề này đã được nạp trước đó.");

            var source = await db.ExamSources.FirstOrDefaultAsync(s => s.Name == sourceName, ct);
            if (source is null)
            {
                source = new Domain.Entities.ExamSource
                {
                    Id = Guid.NewGuid(),
                    Name = sourceName,
                    SourceType = "manual",
                    CreatedAt = DateTimeOffset.UtcNow
                };
                db.ExamSources.Add(source);
                await db.SaveChangesAsync(ct);
            }

            // Store text durably for the background worker.
            var paper = new Domain.Entities.ExamPaper
            {
                Id = Guid.NewGuid(),
                SourceId = source.Id,
                SubjectId = subject.Id,
                Title = title,
                ExamYear = examYear,
                FileHash = fileHash,
                RawContent = contentToProcess,
                Status = Domain.Enums.IngestionStatus.Pending,
                SubmittedAt = DateTimeOffset.UtcNow
            };

            await paperRepo.AddAsync(paper, ct);
            await paperRepo.SaveChangesAsync(ct);

            return Results.Accepted(value: new
            {
                PaperId = paper.Id,
                Status = paper.Status.ToString(),
                Message = "Đề đã vào hàng đợi xử lý."
            });
        }).WithName("IngestExamPaper").DisableAntiforgery();

        /// <summary>
        /// Lấy toàn bộ dữ liệu kết quả xử lý từ Module 1 (5 trạm bóc tách, lọc trùng và sinh biến thể).
        /// </summary>


        return group;
    }
}

public static class QuestionModerationEndpoints
{
    public static RouteGroupBuilder MapQuestionModerationApi(this RouteGroupBuilder group)
    {
        var moderation = group.MapGroup("/admin/questions").WithTags("Question Moderation").RequireAuthorization();
        moderation.MapGet("/pending", async (
            int? limit,
            int? offset,
            ClaimsPrincipal user,
            OmniPrepDbContext db,
            CancellationToken ct) =>
        {
            if (!IsTeacherOrAdmin(user)) return Results.Forbid();
            var pageSize = Math.Clamp(limit ?? 50, 1, 100);
            var pageOffset = Math.Max(offset ?? 0, 0);
            var questions = await db.Questions.AsNoTracking()
                .Where(q => q.IsActive && !q.IsVerified)
                .OrderBy(q => q.CreatedAt).ThenBy(q => q.Id)
                .Skip(pageOffset)
                .Take(pageSize)
                .Select(q => new
                {
                    q.Id, q.Content, q.AttachedTable, q.Format, q.Difficulty, q.Topic,
                    q.AnswerData, q.Explanation, q.DistractorAnalysis, q.ImageUrls,
                    q.SourceTags, q.CreatedAt, SubjectCode = q.Subject.Code, SubjectName = q.Subject.Name
                })
                .ToListAsync(ct);
            return Results.Ok(questions);
        }).WithName("ListPendingQuestions");

        moderation.MapPost("/{questionId:guid}/verify", async (
            Guid questionId,
            ClaimsPrincipal user,
            OmniPrepDbContext db,
            CancellationToken ct) =>
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
        }).WithName("VerifyQuestion");

        moderation.MapPost("/{questionId:guid}/reject", async (
            Guid questionId,
            RejectQuestionRequest? request,
            ClaimsPrincipal user,
            OmniPrepDbContext db,
            CancellationToken ct) =>
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
        }).WithName("RejectQuestion");

        /// <summary>Hiệu đính/chỉnh sửa nội dung câu hỏi trước khi duyệt</summary>
        moderation.MapPut("/{questionId:guid}", async (
            Guid questionId,
            UpdateQuestionRequest req,
            ClaimsPrincipal user,
            OmniPrepDbContext db,
            CancellationToken ct) =>
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
        }).WithName("UpdatePendingQuestion");

        /// <summary>Thống kê số lượng câu hỏi theo trạng thái kiểm duyệt</summary>
        moderation.MapGet("/stats", async (
            ClaimsPrincipal user,
            OmniPrepDbContext db,
            CancellationToken ct) =>
        {
            if (!IsTeacherOrAdmin(user)) return Results.Forbid();

            var totalPending = await db.Questions.CountAsync(q => q.IsActive && !q.IsVerified, ct);
            var totalVerified = await db.Questions.CountAsync(q => q.IsActive && q.IsVerified, ct);
            var totalRejected = await db.Questions.CountAsync(q => !q.IsActive, ct);

            var bySubject = await db.Questions.AsNoTracking()
                .GroupBy(q => new { q.Subject.Code, q.Subject.Name })
                .Select(g => new
                {
                    SubjectCode = g.Key.Code.ToString(),
                    SubjectName = g.Key.Name,
                    Pending = g.Count(q => q.IsActive && !q.IsVerified),
                    Verified = g.Count(q => q.IsActive && q.IsVerified),
                    Rejected = g.Count(q => !q.IsActive)
                })
                .ToListAsync(ct);

            return Results.Ok(new
            {
                TotalPending = totalPending,
                TotalVerified = totalVerified,
                TotalRejected = totalRejected,
                BySubject = bySubject
            });
        }).WithName("GetQuestionModerationStats");

        return group;
    }

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

// ============================================================
// HEALTH - Kiểm tra kết nối Database & Gemini
// ============================================================
public static class HealthEndpoints
{
    public static RouteGroupBuilder MapHealthApi(this RouteGroupBuilder group)
    {
        group.MapGet("/health", async (
            OmniPrepDbContext db,
            IOptions<GeminiOptions> geminiOptions,
            CancellationToken ct) =>
        {
            var dbConnected = false;
            try
            {
                dbConnected = await db.Database.CanConnectAsync(ct);
            }
            catch (Exception)
            {
                dbConnected = false;
            }

            var geminiConfigured = !string.IsNullOrWhiteSpace(geminiOptions.Value.ApiKey)
                && geminiOptions.Value.ApiKey != "YOUR_GEMINI_API_KEY";

            return Results.Ok(new
            {
                Status = dbConnected && geminiConfigured ? "Healthy" : "PartiallyConfigured",
                Timestamp = DateTimeOffset.UtcNow,
                Database = new
                {
                    Connected = dbConnected
                },
                Gemini = new
                {
                    Configured = geminiConfigured,
                    Model = geminiOptions.Value.GenerativeModel,
                    EmbeddingModel = geminiOptions.Value.EmbeddingModel
                }
            });
        }).WithName("HealthCheck").AllowAnonymous().WithTags("System");

        return group;
    }
}

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
            CancellationToken ct) =>
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

            var list = await query
                .OrderByDescending(m => m.CreatedAt)
                .Select(m => new
                {
                    m.Id,
                    m.QuestionId,
                    SubjectCode = m.SubjectCode.ToString(),
                    m.StudentAnswer,
                    m.Mastered,
                    m.Notes,
                    m.CreatedAt,
                    m.UpdatedAt,
                    Question = new
                    {
                        m.Question.Id,
                        m.Question.Content,
                        m.Question.AttachedTable,
                        m.Question.ImageUrls,
                        m.Question.Format,
                        m.Question.Difficulty,
                        m.Question.Topic,
                        Options = m.Question.Format == QuestionFormat.MultipleChoice ? m.Question.AnswerData.Options : null,
                        Statements = m.Question.Format == QuestionFormat.TrueFalse ? m.Question.AnswerData.Statements : null,
                        m.Question.Explanation,
                        m.Question.DistractorAnalysis,
                        m.Question.AnswerData
                    }
                })
                .ToListAsync(ct);

            return Results.Ok(list);
        }).WithName("GetMistakes");

        /// <summary>Đồng bộ câu sai từ kết quả bài thi hoặc từ client</summary>
        mistakes.MapPost("/sync", async (
            SyncMistakesRequest request,
            ClaimsPrincipal user,
            OmniPrepDbContext db,
            CancellationToken ct) =>
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

// ============================================================
// HELPER: Lấy User ID & Role từ JWT Claims (Supabase format)
// ============================================================
internal static class EndpointHelpers
{
    internal static Guid GetUserId(ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? user.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }

    internal static UserRole GetUserRole(ClaimsPrincipal user)
    {
        var roleClaim = user.FindFirstValue("omni_role")
                     ?? user.FindFirstValue(ClaimTypes.Role)
                     ?? user.FindFirstValue("role");
        if (string.Equals(roleClaim, "admin", StringComparison.OrdinalIgnoreCase)) return UserRole.Admin;
        if (string.Equals(roleClaim, "teacher", StringComparison.OrdinalIgnoreCase)) return UserRole.Teacher;

        var appMetadata = user.FindFirst("app_metadata")?.Value;
        if (!string.IsNullOrWhiteSpace(appMetadata))
        {
            try
            {
                using var metadata = System.Text.Json.JsonDocument.Parse(appMetadata);
                if (metadata.RootElement.TryGetProperty("role", out var role))
                {
                    if (string.Equals(role.GetString(), "ADMIN", StringComparison.OrdinalIgnoreCase)) return UserRole.Admin;
                    if (string.Equals(role.GetString(), "TEACHER", StringComparison.OrdinalIgnoreCase)) return UserRole.Teacher;
                }
            }
            catch (System.Text.Json.JsonException) { }
        }
        return UserRole.Student;
    }

    internal static bool IsAdmin(ClaimsPrincipal user) => GetUserRole(user) == UserRole.Admin;
    internal static bool IsTeacherOrAdmin(ClaimsPrincipal user) => GetUserRole(user) is UserRole.Teacher or UserRole.Admin;
}

// ============================================================
// EXAM SOURCES - Danh mục các nguồn đề thi uy tín
// ============================================================
public static class ExamSourcesEndpoints
{
    public static RouteGroupBuilder MapExamSourcesApi(this RouteGroupBuilder group)
    {
        var sources = group.MapGroup("/exam-sources").WithTags("Exam Sources");

        /// <summary>Lấy danh sách nguồn đề thi (Sở GD, trường chuyên, kỳ thi ĐGNL)</summary>
        sources.MapGet("/", async (
            OmniPrepDbContext db,
            string? type,
            string? province,
            string? search,
            CancellationToken ct) =>
        {
            var query = db.ExamSources.AsNoTracking().Where(s => s.IsActive);

            if (!string.IsNullOrWhiteSpace(type))
                query = query.Where(s => s.SourceType == type);

            if (!string.IsNullOrWhiteSpace(province))
                query = query.Where(s => s.Province == province);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                query = query.Where(s => s.Name.ToLower().Contains(term));
            }

            var list = await query
                .OrderBy(s => s.SourceType)
                .ThenBy(s => s.Province)
                .ThenBy(s => s.Name)
                .Select(s => new
                {
                    s.Id,
                    s.Name,
                    s.Province,
                    s.SourceType,
                    s.WebsiteUrl,
                    s.IsActive
                })
                .ToListAsync(ct);

            return Results.Ok(list);
        }).WithName("GetExamSources").AllowAnonymous();

        return group;
    }
}
