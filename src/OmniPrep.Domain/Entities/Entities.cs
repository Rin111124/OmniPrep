using OmniPrep.Domain.Enums;
using OmniPrep.Domain.ValueObjects;

namespace OmniPrep.Domain.Entities;

// ============================================================
// OmniPrep.Domain - Các Entity chính của hệ thống
// Tuân thủ nguyên tắc DDD (Domain-Driven Design)
// ============================================================

/// <summary>
/// Hồ sơ học sinh - liên kết với Supabase Auth
/// </summary>
public class Profile
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public short? Grade { get; set; }
    public string? SchoolName { get; set; }
    /// <summary>Mục tiêu điểm theo môn. Ví dụ: {"GEO": 8.5, "GDKTPL": 9.0}</summary>
    public Dictionary<string, decimal> ScoreGoals { get; set; } = new();
    public string? AvatarUrl { get; set; }
    /// <summary>Vai trò người dùng trong hệ thống phân quyền (RBAC)</summary>
    public UserRole Role { get; set; } = UserRole.Student;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Navigation
    public ICollection<GeneratedExam> GeneratedExams { get; set; } = new List<GeneratedExam>();
    public ICollection<ExamAttempt> ExamAttempts { get; set; } = new List<ExamAttempt>();
    public ICollection<UserTopicMastery> TopicMasteries { get; set; } = new List<UserTopicMastery>();
    public ICollection<UserMistake> Mistakes { get; set; } = new List<UserMistake>();
}

/// <summary>
/// Nguồn đề thi: Sở GD&ĐT, Trường chuyên, Bộ GD&ĐT
/// </summary>
public class ExamSource
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Province { get; set; }
    /// <summary>Loại nguồn: 'so_gd' | 'truong_chuyen' | 'bo_gd' | 'luyen_de'</summary>
    public string SourceType { get; set; } = string.Empty;
    public string? WebsiteUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }

    // Navigation
    public ICollection<ExamPaper> ExamPapers { get; set; } = new List<ExamPaper>();
}

/// <summary>
/// Cấu hình môn học và ma trận đề thi chuẩn Bộ GD&ĐT
/// </summary>
public class Subject
{
    public Guid Id { get; set; }
    public SubjectCode Code { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Ma trận đề thi JSON (số câu/phần, điểm/câu)</summary>
    public ExamMatrix ExamMatrix { get; set; } = new();
    /// <summary>Danh sách chuyên đề của môn học</summary>
    public List<string> Topics { get; set; } = new();
    public decimal TotalScore { get; set; } = 10.00m;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }

    // Navigation
    public ICollection<Question> Questions { get; set; } = new List<Question>();
    public ICollection<GeneratedExam> GeneratedExams { get; set; } = new List<GeneratedExam>();
}

/// <summary>
/// Câu hỏi trong ngân hàng đề - Entity trung tâm của hệ thống
/// </summary>
public class Question
{
    public Guid Id { get; set; }
    public Guid SubjectId { get; set; }

    /// <summary>Nội dung câu hỏi (Markdown, hỗ trợ LaTeX)</summary>
    public string Content { get; set; } = string.Empty;
    /// <summary>Bảng số liệu đính kèm (Markdown table)</summary>
    public string? AttachedTable { get; set; }
    /// <summary>Danh sách URL hình ảnh (Supabase Storage)</summary>
    public List<string> ImageUrls { get; set; } = new();

    public QuestionFormat Format { get; set; }
    public DifficultyLevel Difficulty { get; set; }
    /// <summary>Chuyên đề (phải thuộc Subject.Topics)</summary>
    public string Topic { get; set; } = string.Empty;

    /// <summary>
    /// Dữ liệu đáp án linh hoạt theo format.
    /// MULTIPLE_CHOICE: {"options": {"A":"...","B":"...","C":"...","D":"..."}, "correct": "A"}
    /// TRUE_FALSE: {"statements": [{"id":"a","text":"...","is_correct":true}, ...]}
    /// SHORT_ANSWER: {"value": 1234.56, "tolerance": 0.01, "unit": "km²", "wrong_traps": [...]}
    /// </summary>
    public AnswerData AnswerData { get; set; } = new();

    /// <summary>Lời giải chi tiết (Markdown)</summary>
    public string? Explanation { get; set; }
    /// <summary>Phân tích distractor - lý do học sinh hay chọn sai</summary>
    public Dictionary<string, string> DistractorAnalysis { get; set; } = new();

    // === CHỐNG TRÙNG LẶP ===
    /// <summary>SHA-256 hash của content (Tầng 1: Exact Match)</summary>
    public string ContentHash { get; set; } = string.Empty;
    /// <summary>Vector 768 chiều từ text-embedding-004 (Tầng 2: Semantic Match)</summary>
    public Pgvector.Vector? ContentEmbedding { get; set; }

    // === METADATA NGUỒN GỐC ===
    /// <summary>Danh sách Sở/Trường có chứa câu này. Ví dụ: ["Sở HN 2024", "Sở HCM 2023"]</summary>
    public List<string> SourceTags { get; set; } = new();
    public short? FirstSeenYear { get; set; }
    /// <summary>Số lần xuất hiện trong các đề thi - cập nhật khi merge trùng lặp</summary>
    public int OccurrenceCount { get; set; } = 1;

    public bool IsVerified { get; set; } = false;
    public bool IsActive { get; set; } = true;
    public Guid? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Navigation
    public Subject Subject { get; set; } = null!;
    public ICollection<UserQuestionHistory> History { get; set; } = new List<UserQuestionHistory>();
    public ICollection<UserMistake> UserMistakes { get; set; } = new List<UserMistake>();

    /// <summary>
    /// Tính badge "Câu hỏi trọng tâm" nếu xuất hiện ở nhiều Sở
    /// </summary>
    public bool IsKeyQuestion => OccurrenceCount >= 3;
}

/// <summary>
/// Đề thi gốc được nạp vào hệ thống (log bóc tách)
/// </summary>
public class ExamPaper
{
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public Guid SubjectId { get; set; }
    public string Title { get; set; } = string.Empty;
    public short? ExamYear { get; set; }
    public string? ExamType { get; set; }
    public string? FileUrl { get; set; }
    public string? FileHash { get; set; }
    public string? RawContent { get; set; }

    public IngestionStatus Status { get; set; } = IngestionStatus.Pending;
    public int TotalExtracted { get; set; }
    public int TotalNew { get; set; }
    public int TotalMerged { get; set; }
    public string? ErrorMessage { get; set; }
    public short ProcessingAttempts { get; set; }
    public DateTimeOffset? ProcessingStartedAt { get; set; }
    public Guid? ProcessingLeaseId { get; set; }

    public DateTimeOffset SubmittedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }

    // Navigation
    public ExamSource Source { get; set; } = null!;
    public Subject Subject { get; set; } = null!;
}

/// <summary>
/// Đề thi được trộn ngẫu nhiên theo ma trận cho từng học sinh
/// </summary>
public class GeneratedExam
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid SubjectId { get; set; }
    /// <summary>Danh sách ID câu hỏi theo thứ tự trộn</summary>
    public List<Guid> QuestionIds { get; set; } = new();
    /// <summary>Snapshot ma trận đề tại thời điểm tạo</summary>
    public ExamMatrix MatrixSnapshot { get; set; } = new();
    public short DurationMinutes { get; set; } = 50;
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    // Navigation
    public Profile User { get; set; } = null!;
    public Subject Subject { get; set; } = null!;
    public ICollection<ExamAttempt> Attempts { get; set; } = new List<ExamAttempt>();
}

/// <summary>
/// Lần thi của học sinh - lưu vết toàn bộ lịch sử
/// </summary>
public class ExamAttempt
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid ExamId { get; set; }
    public Guid SubjectId { get; set; }

    // Điểm số từng phần
    public decimal ScorePart1 { get; set; }  // Trắc nghiệm
    public decimal ScorePart2 { get; set; }  // Đúng/Sai
    public decimal ScorePart3 { get; set; }  // Trả lời ngắn
    public decimal TotalScore { get; set; }

    public int? TimeSpentSeconds { get; set; }
    public bool IsSubmitted { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset StartedAt { get; set; }

    // Navigation
    public Profile User { get; set; } = null!;
    public GeneratedExam Exam { get; set; } = null!;
    public ICollection<UserQuestionHistory> QuestionHistories { get; set; } = new List<UserQuestionHistory>();
}

/// <summary>
/// Chi tiết từng câu hỏi học sinh đã làm trong một lần thi
/// </summary>
public class UserQuestionHistory
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid AttemptId { get; set; }
    public Guid QuestionId { get; set; }

    /// <summary>
    /// Câu trả lời của học sinh (JSONB).
    /// MULTIPLE_CHOICE: {"selected": "B"}
    /// TRUE_FALSE: {"answers": {"a": true, "b": false, "c": true, "d": true}}
    /// SHORT_ANSWER: {"value": "1234.5"}
    /// </summary>
    public Dictionary<string, object>? UserAnswer { get; set; }

    public AnswerStatus Status { get; set; } = AnswerStatus.Skipped;
    public decimal PointsEarned { get; set; }
    public int? TimeSpentSeconds { get; set; }
    public bool AiExplanationViewed { get; set; }
    public DateTimeOffset AnsweredAt { get; set; }

    // Navigation
    public Profile User { get; set; } = null!;
    public ExamAttempt Attempt { get; set; } = null!;
    public Question Question { get; set; } = null!;
}

/// <summary>Autosaved answer state for an unsubmitted exam attempt.</summary>
public class AttemptAnswer
{
    public Guid AttemptId { get; set; }
    public Guid UserId { get; set; }
    public Guid QuestionId { get; set; }
    public Dictionary<string, object>? Answer { get; set; }
    public int? TimeSpentSeconds { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Dữ liệu Heatmap năng lực theo chuyên đề - cập nhật sau mỗi lần thi
/// </summary>
public class UserTopicMastery
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid SubjectId { get; set; }
    public string Topic { get; set; } = string.Empty;

    public int TotalAttempts { get; set; }
    public int TotalCorrect { get; set; }
    /// <summary>Tỷ lệ làm đúng (0.0 - 1.0) - dùng cho Heatmap màu sắc</summary>
    public decimal MasteryRate { get; set; }
    /// <summary>Điểm mastery tổng hợp (ELO-like, phục vụ adaptive learning)</summary>
    public decimal MasteryScore { get; set; }

    public DateTimeOffset? LastPracticed { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Navigation
    public Profile User { get; set; } = null!;
    public Subject Subject { get; set; } = null!;

    /// <summary>Phân loại mức độ mastery cho Heatmap</summary>
    public MasteryLevel Level => MasteryRate switch
    {
        >= 0.8m => MasteryLevel.Expert,
        >= 0.6m => MasteryLevel.Proficient,
        >= 0.4m => MasteryLevel.Developing,
        >= 0.2m => MasteryLevel.Beginner,
        _       => MasteryLevel.NotStarted
    };
}

/// <summary>
/// Lịch sử tương tác với AI Tutor
/// </summary>
public class AiTutorSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid QuestionId { get; set; }
    public Guid? AttemptId { get; set; }

    /// <summary>Context đầy đủ đã gửi lên Gemini (để audit và cải thiện prompt)</summary>
    public TutorContext ContextSent { get; set; } = new();
    /// <summary>Phản hồi từ AI (Markdown)</summary>
    public string? AiResponse { get; set; }
    public int? TokensUsed { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    // Navigation
    public Profile User { get; set; } = null!;
    public Question Question { get; set; } = null!;
}
