using System.Text.Json.Serialization;

namespace OmniPrep.Domain.ValueObjects;

// ============================================================
// OmniPrep.Domain - Value Objects
// Các kiểu dữ liệu phức hợp được lưu dạng JSONB trong DB
// ============================================================

/// <summary>
/// Ma trận đề thi theo chuẩn Bộ GD&ĐT.
/// Quy định số câu và điểm theo từng phần.
/// </summary>
public record ExamMatrix
{
    [JsonPropertyName("part1")]
    public ExamPart Part1 { get; init; } = new(18, 0.25m, "MULTIPLE_CHOICE");

    [JsonPropertyName("part2")]
    public ExamPart Part2 { get; init; } = new(4, 1.0m, "TRUE_FALSE");

    [JsonPropertyName("part3")]
    public ExamPart Part3 { get; init; } = new(6, 0.25m, "SHORT_ANSWER");

    /// <summary>Tổng điểm tối đa tính từ ma trận</summary>
    public decimal MaxScore =>
        Part1.Count * Part1.PointsEach +
        Part2.Count * Part2.PointsEach +
        Part3.Count * Part3.PointsEach;
}

public record ExamPart(int Count, decimal PointsEach, string Format);

/// <summary>
/// Dữ liệu đáp án linh hoạt theo format câu hỏi.
/// Được serialize thành JSONB trong database.
/// </summary>
public class AnswerData
{
    // === MULTIPLE_CHOICE ===
    /// <summary>Các lựa chọn A, B, C, D</summary>
    [JsonPropertyName("options")]
    public Dictionary<string, string>? Options { get; set; }

    /// <summary>Đáp án đúng: "A" | "B" | "C" | "D"</summary>
    [JsonPropertyName("correct")]
    public string? CorrectOption { get; set; }

    // === TRUE_FALSE ===
    /// <summary>4 mệnh đề Đúng/Sai</summary>
    [JsonPropertyName("statements")]
    public List<TrueFalseStatement>? Statements { get; set; }

    // === SHORT_ANSWER ===
    /// <summary>Giá trị đáp án số chính xác</summary>
    [JsonPropertyName("value")]
    public decimal? NumericValue { get; set; }

    /// <summary>Dung sai chấp nhận được. Ví dụ: 0.01 cho kết quả ±0.01</summary>
    [JsonPropertyName("tolerance")]
    public decimal? Tolerance { get; set; }

    /// <summary>Đơn vị đo lường. Ví dụ: "km²", "triệu người", "%"</summary>
    [JsonPropertyName("unit")]
    public string? Unit { get; set; }

    /// <summary>
    /// Danh sách các kết quả sai phổ biến do tính nhầm.
    /// Dùng để hỗ trợ AI Tutor phân tích lỗi sai cụ thể.
    /// </summary>
    [JsonPropertyName("wrong_traps")]
    public List<decimal>? WrongTraps { get; set; }

    /// <summary>
    /// Danh sách đáp án văn bản chấp nhận được (dùng khi đáp án là chữ, không phải số).
    /// Ví dụ: ["hà nội", "hanoi", "ha noi"]
    /// </summary>
    [JsonPropertyName("accepted_texts")]
    public List<string>? AcceptedTexts { get; set; }
}

public record TrueFalseStatement
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty; // "a", "b", "c", "d"

    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;

    [JsonPropertyName("is_correct")]
    public bool IsCorrect { get; init; }
}

/// <summary>
/// Context đầy đủ gửi lên Gemini AI Tutor.
/// Chứa đủ thông tin để AI đưa ra phản hồi cá nhân hóa.
/// </summary>
public class TutorContext
{
    /// <summary>Nội dung câu hỏi đầy đủ</summary>
    [JsonPropertyName("question_content")]
    public string QuestionContent { get; set; } = string.Empty;

    /// <summary>Bảng số liệu đính kèm (nếu có)</summary>
    [JsonPropertyName("attached_table")]
    public string? AttachedTable { get; set; }

    /// <summary>Đáp án đúng chi tiết</summary>
    [JsonPropertyName("correct_answer")]
    public object? CorrectAnswer { get; set; }

    /// <summary>Câu trả lời sai của học sinh</summary>
    [JsonPropertyName("student_answer")]
    public object? StudentAnswer { get; set; }

    /// <summary>Lời giải chuẩn (Markdown)</summary>
    [JsonPropertyName("official_explanation")]
    public string? OfficialExplanation { get; set; }

    /// <summary>Tỷ lệ làm đúng của học sinh ở chuyên đề này (0.0-1.0)</summary>
    [JsonPropertyName("student_mastery_rate")]
    public decimal StudentMasteryRate { get; set; }

    /// <summary>Chuyên đề của câu hỏi</summary>
    [JsonPropertyName("topic")]
    public string Topic { get; set; } = string.Empty;

    /// <summary>Phân tích distractor (để AI hiểu tại sao học sinh hay nhầm)</summary>
    [JsonPropertyName("distractor_analysis")]
    public Dictionary<string, string>? DistractorAnalysis { get; set; }

    /// <summary>Tên học sinh (để AI cá nhân hóa lời giải thích)</summary>
    [JsonPropertyName("student_name")]
    public string StudentName { get; set; } = "bạn";
}

/// <summary>
/// Kết quả bóc tách câu hỏi từ Gemini API (response schema)
/// </summary>
public class ExtractedQuestion
{
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("attached_table")]
    public string? AttachedTable { get; set; }

    [JsonPropertyName("format")]
    public string Format { get; set; } = string.Empty; // "MULTIPLE_CHOICE" | "TRUE_FALSE" | "SHORT_ANSWER"

    [JsonPropertyName("difficulty")]
    public string Difficulty { get; set; } = string.Empty;

    [JsonPropertyName("topic")]
    public string Topic { get; set; } = string.Empty;

    [JsonPropertyName("answer_data")]
    public AnswerData AnswerData { get; set; } = new();

    [JsonPropertyName("explanation")]
    public string? Explanation { get; set; }

    [JsonPropertyName("distractor_analysis")]
    public Dictionary<string, string>? DistractorAnalysis { get; set; }
}

/// <summary>
/// Kết quả deduplication cho một câu hỏi
/// </summary>
public record DeduplicationResult(
    bool IsExactDuplicate,
    bool IsSemanticDuplicate,
    Guid? ExistingQuestionId,
    double? SimilarityPercent
);
