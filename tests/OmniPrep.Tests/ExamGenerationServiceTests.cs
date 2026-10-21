using OmniPrep.Domain.Entities;
using OmniPrep.Domain.Enums;
using OmniPrep.Domain.Interfaces;
using OmniPrep.Domain.ValueObjects;
using OmniPrep.Infrastructure.Services;
using Xunit;

namespace OmniPrep.Tests;

public class ExamGenerationServiceTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Subject _geoSubject;

    public ExamGenerationServiceTests()
    {
        _geoSubject = new Subject
        {
            Id = Guid.NewGuid(),
            Code = SubjectCode.GEO,
            Name = "Địa lý",
            Topics = new List<string> { "Địa lý tự nhiên Việt Nam" },
            ExamMatrix = new ExamMatrix
            {
                Part1 = new ExamPart(18, 0.25m, "MULTIPLE_CHOICE"),
                Part2 = new ExamPart(4, 1.0m, "TRUE_FALSE"),
                Part3 = new ExamPart(6, 0.25m, "SHORT_ANSWER")
            },
            TotalScore = 10.0m
        };
    }

    [Fact]
    public async Task GenerateExam_WhenRepositorySuppliesRequiredQuestions_Succeeds()
    {
        // Giả lập QuestionRepository với fallback cung cấp đủ 18 Part 1, 4 Part 2, 6 Part 3
        var fakeQuestionRepo = new FakeQuestionRepository(p1Count: 18, p2Count: 4, p3Count: 6);
        var fakeSubjectRepo = new FakeSubjectRepository(_geoSubject);
        var fakeExamRepo = new FakeExamRepository();

        var service = new ExamGenerationService(fakeQuestionRepo, fakeSubjectRepo, fakeExamRepo);

        var exam = await service.GenerateExamAsync(_userId, SubjectCode.GEO);

        Assert.NotNull(exam);
        Assert.Equal(28, exam.QuestionIds.Count);
        Assert.Equal(_userId, exam.UserId);
        Assert.Equal(_geoSubject.Id, exam.SubjectId);
        Assert.Equal(50, exam.DurationMinutes);
    }

    [Fact]
    public async Task GenerateExam_WhenQuestionBankHasInsufficientQuestions_ThrowsInvalidOperationException()
    {
        // Giả lập chỉ có 5 câu Part 1 (thiếu so với 18 câu yêu cầu)
        var fakeQuestionRepo = new FakeQuestionRepository(p1Count: 5, p2Count: 4, p3Count: 6);
        var fakeSubjectRepo = new FakeSubjectRepository(_geoSubject);
        var fakeExamRepo = new FakeExamRepository();

        var service = new ExamGenerationService(fakeQuestionRepo, fakeSubjectRepo, fakeExamRepo);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GenerateExamAsync(_userId, SubjectCode.GEO));

        Assert.Contains("Ngân hàng câu hỏi đã duyệt chưa đủ", ex.Message);
        Assert.Contains("18/4/6", ex.Message);
        Assert.Contains("5/4/6", ex.Message);
    }

    [Fact]
    public async Task GenerateExam_WhenSubjectDoesNotExist_ThrowsInvalidOperationException()
    {
        var fakeQuestionRepo = new FakeQuestionRepository(18, 4, 6);
        var fakeSubjectRepo = new FakeSubjectRepository(null);
        var fakeExamRepo = new FakeExamRepository();

        var service = new ExamGenerationService(fakeQuestionRepo, fakeSubjectRepo, fakeExamRepo);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GenerateExamAsync(_userId, SubjectCode.MATH));
    }

    // ============================================================
    // FAKE STUBS FOR ISOLATED TESTING
    // ============================================================
    private sealed class FakeQuestionRepository : IQuestionRepository
    {
        private readonly int _p1Count;
        private readonly int _p2Count;
        private readonly int _p3Count;

        public FakeQuestionRepository(int p1Count, int p2Count, int p3Count)
        {
            _p1Count = p1Count;
            _p2Count = p2Count;
            _p3Count = p3Count;
        }

        public Task<IReadOnlyList<Question>> DrawForExamAsync(
            Guid subjectId, Guid userId, QuestionFormat format, int count, string? sourceName = null, CancellationToken ct = default)
        {
            var returnCount = format switch
            {
                QuestionFormat.MultipleChoice => _p1Count,
                QuestionFormat.TrueFalse => _p2Count,
                QuestionFormat.ShortAnswer => _p3Count,
                _ => 0
            };

            var questions = Enumerable.Range(1, returnCount).Select(i => new Question
            {
                Id = Guid.NewGuid(),
                SubjectId = subjectId,
                Format = format,
                Content = $"Test Question {i}",
                Topic = "Test Topic",
                IsActive = true,
                IsVerified = true,
                AnswerData = new AnswerData()
            }).ToList();

            return Task.FromResult<IReadOnlyList<Question>>(questions);
        }

        public Task<Question?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Question?>(null);
        public Task<IReadOnlyList<Question>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Question>>(new List<Question>());
        public Task<Question?> GetByHashAsync(string contentHash, CancellationToken ct = default) => Task.FromResult<Question?>(null);
        public Task<IReadOnlyList<(Guid QuestionId, double CosineDistance, double SimilarityPercent)>> FindSimilarAsync(float[] embedding, Guid subjectId, double threshold = 0.08, int limit = 5, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<(Guid, double, double)>>(new List<(Guid, double, double)>());
        public Task AddAsync(Question question, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(Question question, CancellationToken ct = default) => Task.CompletedTask;
        public Task MergeSourceTagsAsync(Guid existingId, string newSourceTag, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
    }

    private sealed class FakeSubjectRepository : ISubjectRepository
    {
        private readonly Subject? _subject;
        public FakeSubjectRepository(Subject? subject) => _subject = subject;
        public Task<Subject?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(_subject);
        public Task<Subject?> GetByCodeAsync(SubjectCode code, CancellationToken ct = default) => Task.FromResult(_subject != null && _subject.Code == code ? _subject : null);
        public Task<IReadOnlyList<Subject>> GetAllActiveAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Subject>>(_subject != null ? new List<Subject> { _subject } : new List<Subject>());
    }

    private sealed class FakeExamRepository : IExamRepository
    {
        public GeneratedExam? SavedExam { get; private set; }
        public Task<GeneratedExam?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(SavedExam);
        public Task AddAsync(GeneratedExam exam, CancellationToken ct = default)
        {
            SavedExam = exam;
            return Task.CompletedTask;
        }
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
    }
}
