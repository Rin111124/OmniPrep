using OmniPrep.Domain.Entities;
using OmniPrep.Domain.Enums;
using OmniPrep.Domain.ValueObjects;

namespace OmniPrep.Domain.Interfaces;

// ============================================================
// OmniPrep.Domain - Repository & Service Interfaces
// Tuân thủ nguyên tắc Dependency Inversion (SOLID)
// ============================================================

// ============================================================
// REPOSITORY INTERFACES
// ============================================================

public interface IQuestionRepository
{
    Task<Question?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Question>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default);
    Task<Question?> GetByHashAsync(string contentHash, CancellationToken ct = default);

    /// <summary>
    /// Tìm câu hỏi tương đồng bằng vector cosine similarity (Tầng 2 dedup)
    /// </summary>
    Task<IReadOnlyList<(Guid QuestionId, double CosineDistance, double SimilarityPercent)>>
        FindSimilarAsync(float[] embedding, Guid subjectId, double threshold = 0.08, int limit = 5, CancellationToken ct = default);

    /// <summary>
    /// Rút câu hỏi ngẫu nhiên theo format, loại những câu user đã làm đúng, hỗ trợ lọc theo nguồn đề cụ thể
    /// </summary>
    Task<IReadOnlyList<Question>> DrawForExamAsync(
        Guid subjectId, Guid userId, QuestionFormat format, int count, string? sourceName = null, CancellationToken ct = default);

    Task AddAsync(Question question, CancellationToken ct = default);
    Task UpdateAsync(Question question, CancellationToken ct = default);

    /// <summary>Gộp nguồn khi phát hiện câu hỏi trùng lặp</summary>
    Task MergeSourceTagsAsync(Guid existingId, string newSourceTag, CancellationToken ct = default);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface ISubjectRepository
{
    Task<Subject?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Subject?> GetByCodeAsync(SubjectCode code, CancellationToken ct = default);
    Task<IReadOnlyList<Subject>> GetAllActiveAsync(CancellationToken ct = default);
}

public interface IExamRepository
{
    Task<GeneratedExam?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(GeneratedExam exam, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface IAttemptRepository
{
    Task<ExamAttempt?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ExamAttempt>> GetUserAttemptsAsync(Guid userId, int limit = 20, CancellationToken ct = default);
    Task AddAsync(ExamAttempt attempt, CancellationToken ct = default);
    Task UpdateAsync(ExamAttempt attempt, CancellationToken ct = default);

    Task AddQuestionHistoryAsync(UserQuestionHistory history, CancellationToken ct = default);
    Task AddQuestionHistoriesAsync(IEnumerable<UserQuestionHistory> histories, CancellationToken ct = default);
    Task<IReadOnlyList<AttemptAnswer>> GetAttemptAnswersAsync(Guid attemptId, CancellationToken ct = default);
    Task<IReadOnlyList<UserQuestionHistory>> GetAttemptQuestionHistoryAsync(Guid attemptId, CancellationToken ct = default);
    Task SaveAttemptAnswersAsync(Guid userId, Guid attemptId, IReadOnlyList<AttemptAnswer> answers, CancellationToken ct = default);
    Task DeleteAttemptAnswersAsync(Guid attemptId, CancellationToken ct = default);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface IMasteryRepository
{
    Task<IReadOnlyList<UserTopicMastery>> GetUserMasteryAsync(Guid userId, Guid subjectId, CancellationToken ct = default);
    Task UpsertAsync(Guid userId, Guid subjectId, string topic, bool isCorrect, CancellationToken ct = default);
}

public interface IExamPaperRepository
{
    Task<ExamPaper?> GetByFileHashAsync(string fileHash, CancellationToken ct = default);
    Task<IReadOnlyList<ExamPaper>> ClaimPendingPapersAsync(int batchSize = 5, CancellationToken ct = default);
    Task<bool> RefreshProcessingLeaseAsync(Guid paperId, Guid leaseId, CancellationToken ct = default);
    Task<bool> UpdateClaimedPaperAsync(ExamPaper paper, Guid leaseId, CancellationToken ct = default);
    Task AddAsync(ExamPaper paper, CancellationToken ct = default);
    Task UpdateAsync(ExamPaper paper, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

// ============================================================
// SERVICE INTERFACES
// ============================================================

/// <summary>
/// Interface cho AI Embedding Service (text-embedding-004)
/// </summary>
public interface IEmbeddingService
{
    /// <summary>
    /// Tạo vector embedding 768 chiều từ text.
    /// Sử dụng model text-embedding-004 của Google.
    /// </summary>
    Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default);
}

/// <summary>
/// Interface cho Gemini API - Bóc tách đề thi và AI Tutor
/// </summary>
public interface IGeminiService
{
    /// <summary>
    /// Bóc tách câu hỏi từ nội dung PDF/Word sử dụng responseSchema.
    /// Trả về danh sách câu hỏi đã chuẩn hóa.
    /// </summary>
    Task<IReadOnlyList<ExtractedQuestion>> ExtractQuestionsAsync(
        string documentContent,
        SubjectCode subjectCode,
        CancellationToken ct = default);

    /// <summary>
    /// Gọi AI Tutor giải thích lỗi sai theo chế độ Streaming.
    /// </summary>
    IAsyncEnumerable<string> GetTutorExplanationStreamAsync(
        TutorContext context,
        CancellationToken ct = default);

    /// <summary>
    /// Trạm 5: Sinh 2-3 câu hỏi biến thể từ câu hỏi gốc mới (Question Mutation Engine).
    /// </summary>
    Task<IReadOnlyList<ExtractedQuestion>> MutateQuestionAsync(
        ExtractedQuestion originalQuestion,
        SubjectCode subjectCode,
        int variantCount = 2,
        CancellationToken ct = default);
}

/// <summary>
/// Trạm 1: Service tự động quét và cào đề thi PDF/Word từ các nguồn mở
/// </summary>
public interface IScraperService
{
    Task<IReadOnlyList<ScrapedDocument>> ScrapeLatestDocumentsAsync(
        SubjectCode subjectCode,
        int maxFiles = 5,
        CancellationToken ct = default);
}

public record ScrapedDocument(
    string Title,
    string SourceName,
    short? ExamYear,
    SubjectCode SubjectCode,
    string? FileUrl,
    byte[]? ContentBytes,
    string? TextContent
);

/// <summary>
/// Strategy Pattern: Interface cho logic xử lý theo từng môn học.
/// Mỗi môn có một implementation riêng (GeoStrategy, GdktplStrategy...).
/// </summary>
public interface ISubjectStrategy
{
    SubjectCode SubjectCode { get; }

    /// <summary>
    /// Chấm điểm Phần I - Trắc nghiệm (0.25đ/câu)
    /// </summary>
    decimal GradeMultipleChoice(string correctAnswer, string? userAnswer);

    /// <summary>
    /// Chấm điểm Phần II - Đúng/Sai lũy tiến
    /// Quy tắc: 1 ý đúng=0.1đ | 2 ý=0.25đ | 3 ý=0.5đ | 4 ý=1.0đ
    /// </summary>
    decimal GradeTrueFalse(List<TrueFalseStatement> correctStatements,
                           Dictionary<string, bool>? userAnswers);

    /// <summary>
    /// Chấm điểm Phần III - Trả lời ngắn có dung sai.
    /// Chuẩn hóa chuỗi số (chấp nhận cả dấu phẩy và dấu chấm).
    /// </summary>
    decimal GradeShortAnswer(AnswerData answerData, string? userAnswerText);

    /// <summary>
    /// Tạo prompt bóc tách tùy chỉnh theo từng môn.
    /// Ví dụ: Địa lý cần nhận dạng bảng số liệu, GDKT&PL cần nhận dạng tình huống pháp luật.
    /// </summary>
    string BuildExtractionPrompt(string documentContent);
}

/// <summary>
/// Registry để lấy Strategy theo mã môn học (Open/Closed Principle)
/// </summary>
public interface ISubjectStrategyFactory
{
    ISubjectStrategy GetStrategy(SubjectCode subjectCode);
}

/// <summary>
/// Service điều phối toàn bộ pipeline bóc tách và chống trùng lặp
/// </summary>
public interface IIngestionService
{
    Task<IngestionResult> IngestExamPaperAsync(
        Guid examPaperId,
        string documentContent,
        SubjectCode subjectCode,
        string sourceName,
        short? examYear,
        CancellationToken ct = default);
}

public record IngestionResult(
    int TotalExtracted,
    int TotalNew,
    int TotalMerged,
    int TotalFailed,
    IReadOnlyList<string> Errors
);

/// <summary>
/// Service tạo đề thi theo ma trận và lịch sử học sinh
/// </summary>
public interface IExamGenerationService
{
    Task<GeneratedExam> GenerateExamAsync(
        Guid userId,
        SubjectCode subjectCode,
        string? sourceName = null,
        CancellationToken ct = default);
}

/// <summary>
/// Service chấm điểm toàn bộ bài thi (3 phần)
/// </summary>
public interface IGradingService
{
    Task<GradingResult> GradeAttemptAsync(
        Guid attemptId,
        IReadOnlyList<UserSubmission> submissions,
        CancellationToken ct = default);
}

public record UserSubmission(
    Guid QuestionId,
    QuestionFormat Format,
    Dictionary<string, object>? Answer,
    int? TimeSpentSeconds
);

public record GradingResult(
    Guid AttemptId,
    decimal ScorePart1,
    decimal ScorePart2,
    decimal ScorePart3,
    decimal TotalScore,
    IReadOnlyList<QuestionGradeDetail> Details
);

public record QuestionGradeDetail(
    Guid QuestionId,
    AnswerStatus Status,
    decimal PointsEarned,
    string Topic,
    DifficultyLevel Difficulty
);
