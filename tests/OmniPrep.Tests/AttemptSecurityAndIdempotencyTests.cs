using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using OmniPrep.Api.Endpoints;
using OmniPrep.Domain.Entities;
using OmniPrep.Domain.Enums;
using OmniPrep.Domain.Interfaces;
using OmniPrep.Domain.ValueObjects;
using Xunit;

namespace OmniPrep.Tests;

/// <summary>
/// Bộ kiểm thử an ninh và tính nhất quán của bài thi (Giai đoạn 1 & Giai đoạn 3):
/// 1. Phân quyền và cô lập tài khoản (Cross-account isolation).
/// 2. Chống nộp bài lặp lại (Submit Idempotency - 409 Conflict).
/// 3. Kiểm soát thời gian làm bài & hạn chót (Deadline & Expiry enforcement).
/// 4. Xác thực dữ liệu Autosave (chống duplicate, chống tráo câu hỏi).
/// </summary>
public class AttemptSecurityAndIdempotencyTests
{
    private readonly Guid _ownerUserId = Guid.NewGuid();
    private readonly Guid _attackerUserId = Guid.NewGuid();
    private readonly Guid _examId = Guid.NewGuid();
    private readonly Guid _attemptId = Guid.NewGuid();
    private readonly List<Guid> _examQuestionIds;

    public AttemptSecurityAndIdempotencyTests()
    {
        _examQuestionIds = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
    }

    private GeneratedExam CreateTestExam(bool expired = false, short durationMinutes = 50) => new()
    {
        Id = _examId,
        UserId = _ownerUserId,
        SubjectId = Guid.NewGuid(),
        DurationMinutes = durationMinutes,
        ExpiresAt = expired ? DateTimeOffset.UtcNow.AddMinutes(-5) : DateTimeOffset.UtcNow.AddHours(2),
        QuestionIds = _examQuestionIds,
        MatrixSnapshot = new ExamMatrix
        {
            Part1 = new ExamPart(18, 0.25m, "MULTIPLE_CHOICE"),
            Part2 = new ExamPart(4, 1.0m, "TRUE_FALSE"),
            Part3 = new ExamPart(6, 0.25m, "SHORT_ANSWER")
        }
    };

    private ExamAttempt CreateTestAttempt(bool isSubmitted = false, DateTimeOffset? startedAt = null, GeneratedExam? exam = null)
    {
        var e = exam ?? CreateTestExam();
        return new ExamAttempt
        {
            Id = _attemptId,
            UserId = _ownerUserId,
            ExamId = e.Id,
            SubjectId = e.SubjectId,
            Exam = e,
            IsSubmitted = isSubmitted,
            StartedAt = startedAt ?? DateTimeOffset.UtcNow.AddMinutes(-10)
        };
    }

    private static ClaimsPrincipal CreateUserPrincipal(Guid userId) =>
        new(new ClaimsIdentity([new Claim("sub", userId.ToString())], "TestAuth"));

    // =========================================================================
    // 1. CROSS-ACCOUNT ISOLATION (CHỐNG TRUY CẬP CHÉO DỮ LIỆU GIỮA 2 TÀI KHOẢN)
    // =========================================================================

    [Fact]
    public async Task GetAttempt_WhenAccessedByAnotherUser_ReturnsForbid()
    {
        var attempt = CreateTestAttempt();
        var fakeAttemptRepo = new FakeAttemptRepository(attempt);
        var fakeQuestionRepo = new FakeQuestionRepo();
        var attackerPrincipal = CreateUserPrincipal(_attackerUserId);

        var result = await GradingEndpoints.HandleGetAttemptAsync(
            _attemptId, fakeAttemptRepo, fakeQuestionRepo, attackerPrincipal, CancellationToken.None);

        Assert.IsType<ForbidHttpResult>(result);
    }

    [Fact]
    public async Task SaveAttemptAnswers_WhenCalledByAnotherUser_ReturnsForbid()
    {
        var attempt = CreateTestAttempt();
        var fakeAttemptRepo = new FakeAttemptRepository(attempt);
        var attackerPrincipal = CreateUserPrincipal(_attackerUserId);
        var req = new GradingEndpoints.SaveAttemptAnswersRequest(
            [new GradingEndpoints.AttemptAnswerSubmission(_examQuestionIds[0], new Dictionary<string, object> { ["selected"] = "A" }, 10)], 10);

        var result = await GradingEndpoints.HandleSaveAttemptAnswersAsync(
            _attemptId, req, fakeAttemptRepo, attackerPrincipal, CancellationToken.None);

        Assert.IsType<ForbidHttpResult>(result);
    }

    [Fact]
    public async Task SubmitAttempt_WhenCalledByAnotherUser_ReturnsForbid()
    {
        var attempt = CreateTestAttempt();
        var fakeAttemptRepo = new FakeAttemptRepository(attempt);
        var fakeQuestionRepo = new FakeQuestionRepo();
        var fakeGradingService = new FakeGradingService();
        var attackerPrincipal = CreateUserPrincipal(_attackerUserId);
        var req = new GradingEndpoints.SubmitAttemptRequest([], 10);

        var result = await GradingEndpoints.HandleSubmitAttemptAsync(
            _attemptId, req, fakeAttemptRepo, fakeQuestionRepo, fakeGradingService, attackerPrincipal, CancellationToken.None);

        Assert.IsType<ForbidHttpResult>(result);
    }

    [Fact]
    public async Task GetAttemptResult_WhenCalledByAnotherUser_ReturnsForbid()
    {
        var attempt = CreateTestAttempt(isSubmitted: true);
        var fakeAttemptRepo = new FakeAttemptRepository(attempt);
        var fakeQuestionRepo = new FakeQuestionRepo();
        var attackerPrincipal = CreateUserPrincipal(_attackerUserId);

        var result = await GradingEndpoints.HandleGetAttemptResultAsync(
            _attemptId, fakeAttemptRepo, fakeQuestionRepo, attackerPrincipal, CancellationToken.None);

        Assert.IsType<ForbidHttpResult>(result);
    }

    [Fact]
    public async Task AnyEndpoint_WhenUnauthenticated_ReturnsUnauthorized()
    {
        var attempt = CreateTestAttempt();
        var fakeAttemptRepo = new FakeAttemptRepository(attempt);
        var unauthPrincipal = new ClaimsPrincipal(new ClaimsIdentity());

        var result = await GradingEndpoints.HandleGetAttemptAsync(
            _attemptId, fakeAttemptRepo, new FakeQuestionRepo(), unauthPrincipal, CancellationToken.None);

        Assert.IsType<UnauthorizedHttpResult>(result);
    }

    // =========================================================================
    // 2. SUBMIT IDEMPOTENCY & DUPLICATE SUBMIT DEFENSE
    // =========================================================================

    [Fact]
    public async Task SubmitAttempt_WhenAlreadySubmitted_ReturnsConflict409()
    {
        // Mô phỏng học sinh submit 2 lần liên tiếp (double click hoặc gửi lặp request)
        var attempt = CreateTestAttempt(isSubmitted: true);
        var fakeAttemptRepo = new FakeAttemptRepository(attempt);
        var fakeQuestionRepo = new FakeQuestionRepo();
        var fakeGradingService = new FakeGradingService();
        var ownerPrincipal = CreateUserPrincipal(_ownerUserId);
        var req = new GradingEndpoints.SubmitAttemptRequest([], 10);

        var result = await GradingEndpoints.HandleSubmitAttemptAsync(
            _attemptId, req, fakeAttemptRepo, fakeQuestionRepo, fakeGradingService, ownerPrincipal, CancellationToken.None);

        var conflict = Assert.IsType<Conflict<string>>(result);
        Assert.Equal("Bài thi đã được nộp.", conflict.Value);
        Assert.False(fakeGradingService.WasCalled, "GradingService tuyệt đối không được gọi lại khi bài đã nộp!");
    }

    [Fact]
    public async Task SaveAttemptAnswers_WhenAttemptAlreadySubmitted_ReturnsConflict()
    {
        // Khi bài đã nộp, cấm mọi thao tác ghi đè câu trả lời
        var attempt = CreateTestAttempt(isSubmitted: true);
        var fakeAttemptRepo = new FakeAttemptRepository(attempt);
        var ownerPrincipal = CreateUserPrincipal(_ownerUserId);
        var req = new GradingEndpoints.SaveAttemptAnswersRequest([], 10);

        var result = await GradingEndpoints.HandleSaveAttemptAnswersAsync(
            _attemptId, req, fakeAttemptRepo, ownerPrincipal, CancellationToken.None);

        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(409, ((IStatusCodeHttpResult)result).StatusCode);
    }

    // =========================================================================
    // 3. DEADLINE & EXPOSURE EXPIRATION ENFORCEMENT
    // =========================================================================

    [Fact]
    public async Task SubmitAttempt_WhenExamExpired_ReturnsConflict()
    {
        var expiredExam = CreateTestExam(expired: true);
        var attempt = CreateTestAttempt(exam: expiredExam);
        var fakeAttemptRepo = new FakeAttemptRepository(attempt);
        var ownerPrincipal = CreateUserPrincipal(_ownerUserId);
        var req = new GradingEndpoints.SubmitAttemptRequest([], 10);

        var result = await GradingEndpoints.HandleSubmitAttemptAsync(
            _attemptId, req, fakeAttemptRepo, new FakeQuestionRepo(), new FakeGradingService(), ownerPrincipal, CancellationToken.None);

        var conflict = Assert.IsType<Conflict<string>>(result);
        Assert.Equal("Bài thi đã hết hạn.", conflict.Value);
    }

    [Fact]
    public async Task SubmitAttempt_WhenDurationExceeded_ReturnsConflict()
    {
        // Đề 50 phút, bắt đầu từ 55 phút trước (quá hạn 50m + 30s buffer)
        var attempt = CreateTestAttempt(startedAt: DateTimeOffset.UtcNow.AddMinutes(-55));
        var fakeAttemptRepo = new FakeAttemptRepository(attempt);
        var ownerPrincipal = CreateUserPrincipal(_ownerUserId);
        var req = new GradingEndpoints.SubmitAttemptRequest([], 10);

        var result = await GradingEndpoints.HandleSubmitAttemptAsync(
            _attemptId, req, fakeAttemptRepo, new FakeQuestionRepo(), new FakeGradingService(), ownerPrincipal, CancellationToken.None);

        var conflict = Assert.IsType<Conflict<string>>(result);
        Assert.Equal("Attempt has expired.", conflict.Value);
    }

    // =========================================================================
    // 4. AUTOSAVE PAYLOAD VALIDATION & SERVER-SIDE INTEGRITY
    // =========================================================================

    [Fact]
    public async Task SaveAttemptAnswers_WithDuplicateQuestions_ReturnsBadRequest()
    {
        var attempt = CreateTestAttempt();
        var fakeAttemptRepo = new FakeAttemptRepository(attempt);
        var ownerPrincipal = CreateUserPrincipal(_ownerUserId);
        var qId = _examQuestionIds[0];

        // Gửi 2 câu trả lời cho cùng một QuestionId
        var req = new GradingEndpoints.SaveAttemptAnswersRequest([
            new GradingEndpoints.AttemptAnswerSubmission(qId, new Dictionary<string, object> { ["selected"] = "A" }, 10),
            new GradingEndpoints.AttemptAnswerSubmission(qId, new Dictionary<string, object> { ["selected"] = "B" }, 15)
        ], 25);

        var result = await GradingEndpoints.HandleSaveAttemptAnswersAsync(
            _attemptId, req, fakeAttemptRepo, ownerPrincipal, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequest<string>>(result);
        Assert.Equal("Duplicate question answers.", badRequest.Value);
    }

    [Fact]
    public async Task SaveAttemptAnswers_WithOutsideQuestion_ReturnsBadRequest()
    {
        var attempt = CreateTestAttempt();
        var fakeAttemptRepo = new FakeAttemptRepository(attempt);
        var ownerPrincipal = CreateUserPrincipal(_ownerUserId);
        var rogueQuestionId = Guid.NewGuid(); // Không nằm trong _examQuestionIds

        var req = new GradingEndpoints.SaveAttemptAnswersRequest([
            new GradingEndpoints.AttemptAnswerSubmission(rogueQuestionId, new Dictionary<string, object> { ["selected"] = "A" }, 10)
        ], 10);

        var result = await GradingEndpoints.HandleSaveAttemptAnswersAsync(
            _attemptId, req, fakeAttemptRepo, ownerPrincipal, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequest<string>>(result);
        Assert.Equal("Answer includes a question outside this exam.", badRequest.Value);
    }

    [Fact]
    public async Task SaveAttemptAnswers_ValidAnswers_SuccessfullyPersists()
    {
        var attempt = CreateTestAttempt();
        var fakeAttemptRepo = new FakeAttemptRepository(attempt);
        var ownerPrincipal = CreateUserPrincipal(_ownerUserId);

        var req = new GradingEndpoints.SaveAttemptAnswersRequest([
            new GradingEndpoints.AttemptAnswerSubmission(_examQuestionIds[0], new Dictionary<string, object> { ["selected"] = "C" }, 20),
            new GradingEndpoints.AttemptAnswerSubmission(_examQuestionIds[1], new Dictionary<string, object> { ["answers"] = new Dictionary<string, bool> { ["a"] = true } }, 35)
        ], 55);

        var result = await GradingEndpoints.HandleSaveAttemptAnswersAsync(
            _attemptId, req, fakeAttemptRepo, ownerPrincipal, CancellationToken.None);

        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(200, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.Equal(2, fakeAttemptRepo.LastSavedAnswers.Count);
        Assert.Equal(_ownerUserId, fakeAttemptRepo.LastSavedUserId);
        Assert.Equal(_attemptId, fakeAttemptRepo.LastSavedAttemptId);
    }

    // =========================================================================
    // FAKE REPOSITORIES & SERVICES FOR ISOLATED TESTING
    // =========================================================================

    private sealed class FakeAttemptRepository : IAttemptRepository
    {
        private readonly ExamAttempt? _attempt;
        public IReadOnlyList<AttemptAnswer> LastSavedAnswers { get; private set; } = [];
        public Guid LastSavedUserId { get; private set; }
        public Guid LastSavedAttemptId { get; private set; }

        public FakeAttemptRepository(ExamAttempt? attempt) => _attempt = attempt;

        public Task<ExamAttempt?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(_attempt);
        public Task<IReadOnlyList<ExamAttempt>> GetUserAttemptsAsync(Guid userId, int limit = 20, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ExamAttempt>>([]);
        public Task AddAsync(ExamAttempt attempt, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(ExamAttempt attempt, CancellationToken ct = default) => Task.CompletedTask;
        public Task AddQuestionHistoryAsync(UserQuestionHistory history, CancellationToken ct = default) => Task.CompletedTask;
        public Task AddQuestionHistoriesAsync(IEnumerable<UserQuestionHistory> histories, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<AttemptAnswer>> GetAttemptAnswersAsync(Guid attemptId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AttemptAnswer>>([]);
        public Task<IReadOnlyList<UserQuestionHistory>> GetAttemptQuestionHistoryAsync(Guid attemptId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<UserQuestionHistory>>([]);
        public Task SaveAttemptAnswersAsync(Guid userId, Guid attemptId, IReadOnlyList<AttemptAnswer> answers, CancellationToken ct = default)
        {
            LastSavedUserId = userId;
            LastSavedAttemptId = attemptId;
            LastSavedAnswers = answers;
            return Task.CompletedTask;
        }
        public Task DeleteAttemptAnswersAsync(Guid attemptId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
    }

    private sealed class FakeQuestionRepo : IQuestionRepository
    {
        public Task<Question?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Question?>(null);
        public Task<IReadOnlyList<Question>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Question>>([]);
        public Task<Question?> GetByHashAsync(string contentHash, CancellationToken ct = default) => Task.FromResult<Question?>(null);
        public Task<IReadOnlyList<Question>> DrawForExamAsync(Guid subjectId, Guid userId, QuestionFormat format, int count, string? sourceName = null, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Question>>([]);
        public Task<IReadOnlyList<(Guid QuestionId, double CosineDistance, double SimilarityPercent)>> FindSimilarAsync(float[] embedding, Guid subjectId, double threshold = 0.08, int limit = 5, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<(Guid, double, double)>>([]);
        public Task AddAsync(Question question, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(Question question, CancellationToken ct = default) => Task.CompletedTask;
        public Task MergeSourceTagsAsync(Guid existingId, string newSourceTag, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
    }

    private sealed class FakeGradingService : IGradingService
    {
        public bool WasCalled { get; private set; }

        public Task<GradingResult> GradeAttemptAsync(Guid attemptId, IReadOnlyList<UserSubmission> submissions, CancellationToken ct = default)
        {
            WasCalled = true;
            return Task.FromResult(new GradingResult(attemptId, 4.5m, 4.0m, 1.5m, 10.0m, []));
        }
    }
}
