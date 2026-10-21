using Microsoft.Extensions.Logging;
using System.Text.Json;
using OmniPrep.Domain.Entities;
using OmniPrep.Domain.Enums;
using OmniPrep.Domain.Interfaces;
using OmniPrep.Domain.ValueObjects;

namespace OmniPrep.Infrastructure.Services;

// ============================================================
// OmniPrep.Infrastructure - Grading Service
// Chấm điểm 3 phần theo đúng quy tắc Bộ GD&ĐT
// ============================================================

/// <summary>
/// Service chấm điểm toàn bộ bài thi theo 3 phần.
/// Delegate logic chấm điểm chi tiết cho ISubjectStrategy (Strategy Pattern).
/// </summary>
public sealed class GradingService : IGradingService
{
    private readonly IAttemptRepository _attemptRepo;
    private readonly IQuestionRepository _questionRepo;
    private readonly ISubjectStrategyFactory _strategyFactory;
    private readonly IMasteryRepository _masteryRepo;
    private readonly ILogger<GradingService> _logger;

    public GradingService(
        IAttemptRepository attemptRepo,
        IQuestionRepository questionRepo,
        ISubjectStrategyFactory strategyFactory,
        IMasteryRepository masteryRepo,
        ILogger<GradingService> logger)
    {
        _attemptRepo = attemptRepo;
        _questionRepo = questionRepo;
        _strategyFactory = strategyFactory;
        _masteryRepo = masteryRepo;
        _logger = logger;
    }

    public async Task<GradingResult> GradeAttemptAsync(
        Guid attemptId,
        IReadOnlyList<UserSubmission> submissions,
        CancellationToken ct = default)
    {
        // Load attempt và exam
        var attempt = await _attemptRepo.GetByIdAsync(attemptId, ct)
            ?? throw new KeyNotFoundException($"Không tìm thấy lần thi: {attemptId}");

        if (attempt.IsSubmitted)
            throw new InvalidOperationException("Bài thi đã được nộp, không thể chấm lại.");

        // Grade the full generated exam so omitted answers are persisted as skipped.
        var questionIds = attempt.Exam.QuestionIds;
        var questions = await _questionRepo.GetByIdsAsync(questionIds, ct);
        var questionDict = questions.ToDictionary(q => q.Id);
        var submissionDict = submissions.ToDictionary(s => s.QuestionId);

        // Lấy strategy cho môn học
        var strategy = _strategyFactory.GetStrategy(
            Enum.Parse<SubjectCode>(attempt.Exam.Subject.Code.ToString()));

        decimal scorePart1 = 0, scorePart2 = 0, scorePart3 = 0;
        var details = new List<QuestionGradeDetail>();
        var histories = new List<UserQuestionHistory>();

        // =====================================================
        // CHẤM TỪNG CÂU HỎI
        // =====================================================
        foreach (var questionId in questionIds)
        {
            if (!questionDict.TryGetValue(questionId, out var question))
            {
                _logger.LogWarning("Không tìm thấy câu hỏi {Id} khi chấm điểm", questionId);
                continue;
            }
            var submission = submissionDict.TryGetValue(questionId, out var submittedAnswer)
                ? submittedAnswer
                : new UserSubmission(questionId, question.Format, null, null);

            decimal points = 0;
            AnswerStatus status;

            switch (question.Format)
            {
                // -----------------------------------------------
                // PHẦN I: Trắc nghiệm 4 lựa chọn (0.25đ/câu)
                // -----------------------------------------------
                case QuestionFormat.MultipleChoice:
                {
                    var userSelected = submission.Answer?.GetValueOrDefault("selected")?.ToString();
                    points = strategy.GradeMultipleChoice(
                        question.AnswerData.CorrectOption!, userSelected);
                    status = DetermineStatus(points, submission.Answer);
                    scorePart1 += points;
                    break;
                }

                // -----------------------------------------------
                // PHẦN II: Đúng/Sai lũy tiến
                // Quy tắc: 1 ý đúng=0.1đ | 2 ý=0.25đ | 3 ý=0.5đ | 4 ý=1.0đ
                // -----------------------------------------------
                case QuestionFormat.TrueFalse:
                {
                    var userAnswers = ReadBooleanAnswers(submission.Answer);
                    points = strategy.GradeTrueFalse(
                        question.AnswerData.Statements!, userAnswers);
                    status = points >= 1m
                        ? AnswerStatus.Correct
                        : submission.Answer is null || submission.Answer.Count == 0
                            ? AnswerStatus.Skipped
                            : AnswerStatus.Wrong;
                    scorePart2 += points;
                    break;
                }

                // -----------------------------------------------
                // PHẦN III: Trả lời ngắn có dung sai
                // -----------------------------------------------
                case QuestionFormat.ShortAnswer:
                {
                    var userText = submission.Answer?.GetValueOrDefault("value")?.ToString();
                    points = strategy.GradeShortAnswer(question.AnswerData, userText);
                    status = DetermineStatus(points, submission.Answer);
                    scorePart3 += points;
                    break;
                }

                default:
                    status = AnswerStatus.Skipped;
                    break;
            }

            details.Add(new QuestionGradeDetail(
                question.Id, status, points, question.Topic, question.Difficulty));

            histories.Add(new UserQuestionHistory
            {
                Id = Guid.NewGuid(),
                UserId = attempt.UserId,
                AttemptId = attemptId,
                QuestionId = question.Id,
                UserAnswer = submission.Answer,
                Status = status,
                PointsEarned = points,
                TimeSpentSeconds = submission.TimeSpentSeconds,
                AnsweredAt = DateTimeOffset.UtcNow
            });

            // Cập nhật mastery theo chuyên đề
            await _masteryRepo.UpsertAsync(
                attempt.UserId,
                question.SubjectId,
                question.Topic,
                status == AnswerStatus.Correct,
                ct);
        }

        // Cập nhật attempt
        var totalScore = Math.Round(scorePart1 + scorePart2 + scorePart3, 2);
        attempt.ScorePart1 = Math.Round(scorePart1, 2);
        attempt.ScorePart2 = Math.Round(scorePart2, 2);
        attempt.ScorePart3 = Math.Round(scorePart3, 2);
        attempt.TotalScore = totalScore;
        attempt.IsSubmitted = true;
        attempt.SubmittedAt = DateTimeOffset.UtcNow;

        // Lưu lịch sử câu hỏi và cập nhật attempt
        await _attemptRepo.AddQuestionHistoriesAsync(histories, ct);
        await _attemptRepo.DeleteAttemptAnswersAsync(attemptId, ct);
        await _attemptRepo.UpdateAsync(attempt, ct);
        await _attemptRepo.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Chấm xong bài thi {AttemptId}: Phần1={P1}, Phần2={P2}, Phần3={P3}, Tổng={Total}",
            attemptId, scorePart1, scorePart2, scorePart3, totalScore);

        return new GradingResult(attemptId, scorePart1, scorePart2, scorePart3, totalScore, details);
    }

    /// <summary>Xác định trạng thái đúng/sai/bỏ qua từ điểm số và câu trả lời</summary>
    private static AnswerStatus DetermineStatus(decimal points, Dictionary<string, object>? answer)
    {
        if (answer is null || answer.Count == 0) return AnswerStatus.Skipped;
        return points > 0 ? AnswerStatus.Correct : AnswerStatus.Wrong;
    }

    private static Dictionary<string, bool>? ReadBooleanAnswers(Dictionary<string, object>? answer)
    {
        if (answer?.TryGetValue("answers", out var value) != true) return null;

        if (value is JsonElement element && element.ValueKind == JsonValueKind.Object)
        {
            var parsed = new Dictionary<string, bool>();
            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    parsed[property.Name] = property.Value.GetBoolean();
                else if (bool.TryParse(property.Value.ToString(), out var boolean))
                    parsed[property.Name] = boolean;
            }
            return parsed;
        }

        if (value is IReadOnlyDictionary<string, object> dictionary)
            return dictionary.ToDictionary(
                pair => pair.Key,
                pair => bool.TryParse(pair.Value.ToString(), out var boolean) && boolean);

        return null;
    }
}

// ============================================================
// STRATEGY IMPLEMENTATIONS
// Mỗi môn học có logic chấm điểm và prompt bóc tách riêng
// ============================================================

/// <summary>
/// Base implementation cho phần chấm điểm chung 3 phần.
/// Các môn học kế thừa và override BuildExtractionPrompt() cho riêng mình.
/// </summary>
public abstract class BaseSubjectStrategy : ISubjectStrategy
{
    public abstract SubjectCode SubjectCode { get; }

    /// <summary>
    /// Chấm điểm Phần I: Trắc nghiệm (0.25đ/câu)
    /// Logic hoàn toàn giống nhau cho mọi môn.
    /// </summary>
    public virtual decimal GradeMultipleChoice(string correctAnswer, string? userAnswer)
    {
        if (string.IsNullOrWhiteSpace(userAnswer)) return 0m;
        return string.Equals(correctAnswer.Trim(), userAnswer.Trim(), StringComparison.OrdinalIgnoreCase)
            ? 0.25m : 0m;
    }

    /// <summary>
    /// Chấm điểm Phần II: Đúng/Sai lũy tiến
    /// Quy tắc Bộ GD&ĐT: 1 ý đúng=0.1đ | 2 ý=0.25đ | 3 ý=0.5đ | 4 ý=1.0đ
    /// </summary>
    public virtual decimal GradeTrueFalse(
        List<TrueFalseStatement> correctStatements,
        Dictionary<string, bool>? userAnswers)
    {
        if (userAnswers is null || userAnswers.Count == 0) return 0m;

        int correctCount = correctStatements.Count(stmt =>
            userAnswers.TryGetValue(stmt.Id, out bool userVal) && userVal == stmt.IsCorrect);

        return correctCount switch
        {
            4 => 1.00m,  // Cả 4 ý đúng
            3 => 0.50m,  // 3 ý đúng
            2 => 0.25m,  // 2 ý đúng
            1 => 0.10m,  // 1 ý đúng
            _ => 0.00m   // Không có ý nào đúng
        };
    }

    /// <summary>
    /// Chấm điểm Phần III: Trả lời ngắn số có dung sai.
    /// Chuẩn hóa: chấp nhận cả dấu phẩy và dấu chấm thập phân.
    /// </summary>
    public virtual decimal GradeShortAnswer(AnswerData answerData, string? userAnswerText)
    {
        if (string.IsNullOrWhiteSpace(userAnswerText)) return 0m;

        // === Trường hợp đáp án text ===
        if (answerData.AcceptedTexts?.Count > 0)
        {
            var normalizedUser = userAnswerText.Trim().ToLowerInvariant();
            var isTextMatch = answerData.AcceptedTexts
                .Any(t => string.Equals(t.Trim(), normalizedUser, StringComparison.OrdinalIgnoreCase));
            return isTextMatch ? 0.25m : 0m;
        }

        // === Trường hợp đáp án số ===
        if (answerData.NumericValue is null) return 0m;

        // Chuẩn hóa: thay dấu phẩy bằng dấu chấm (hỗ trợ cả "1.234,56" và "1234.56")
        var normalizedText = userAnswerText
            .Trim()
            .Replace(" ", "")
            .Replace(",", ".");

        // Xử lý trường hợp "1.234.56" (có cả dấu phân cách ngàn và thập phân)
        // Giả sử dấu chấm cuối cùng là dấu thập phân
        var lastDot = normalizedText.LastIndexOf('.');
        if (lastDot > 0 && normalizedText.IndexOf('.') != lastDot)
        {
            normalizedText = normalizedText.Replace(".", "");
            normalizedText = normalizedText[..lastDot] + "." + normalizedText[lastDot..];
        }

        if (!decimal.TryParse(normalizedText,
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture,
                out decimal parsedValue))
        {
            return 0m;
        }

        var tolerance = answerData.Tolerance ?? 0.005m;
        var diff = Math.Abs(parsedValue - answerData.NumericValue.Value);

        return diff <= tolerance ? 0.25m : 0m;
    }

    public abstract string BuildExtractionPrompt(string documentContent);
}

/// <summary>
/// Strategy cho môn Địa lý.
/// Đặc thù: Nhận dạng bảng số liệu, biểu đồ, câu hỏi Atlat.
/// </summary>
public sealed class GeographyStrategy : BaseSubjectStrategy
{
    public override SubjectCode SubjectCode => SubjectCode.GEO;

    public override string BuildExtractionPrompt(string documentContent)
    {
        return $"""
        Bạn là chuyên gia bóc tách đề thi Địa lý THPT của Việt Nam.
        Hãy phân tích đề thi sau và trích xuất TOÀN BỘ, ĐẦY ĐỦ TẤT CẢ câu hỏi theo cấu trúc JSON được chỉ định.
        TUYỆT ĐỐI KHÔNG BỎ SÓT BẤT KỲ CÂU NÀO dù đề thi có 28, 40 hay 50+ câu hỏi.

        QUY TẮC BÓC TÁCH ĐỀ ĐỊA LÝ:
        1. Phần I: Trắc nghiệm 4 lựa chọn A, B, C, D (format: "MULTIPLE_CHOICE")
        2. Phần II: Đúng/Sai 4 ý a, b, c, d (format: "TRUE_FALSE") - thường kèm bảng số liệu
        3. Phần III: Trả lời ngắn số/chữ (format: "SHORT_ANSWER") - thường yêu cầu tính toán từ số liệu
        
        ĐẶC BIỆT LƯU Ý:
        - Bóc tách đầy đủ tất cả câu hỏi từ đầu đến cuối văn bản, không tóm tắt hay dừng giữa chừng.
        - Nếu câu hỏi có bảng số liệu: Copy nguyên xi nội dung bảng vào trường "attached_table" (dạng Markdown table)
        - Phân loại chuyên đề chính xác: "Địa lý tự nhiên Việt Nam", "Địa lý dân cư Việt Nam", 
          "Địa lý kinh tế Việt Nam", "Địa lý các vùng kinh tế", "Địa lý biển và hải đảo",
          "Atlat Địa lý", "Kỹ năng đọc biểu đồ", "Kỹ năng nhận xét bảng số liệu"
        - Phần III: Ghi rõ đơn vị (nghìn tấn, triệu người, km², %) và dung sai cho phép
        - Ghi chú distractor: Giải thích tại sao các đáp án nhiễu B, C, D dễ nhầm lẫn

        NỘI DUNG ĐỀ THI:
        {documentContent}

        Trả về JSON array, mỗi phần tử là một câu hỏi đúng theo schema đã cho.
        """;
    }
}

/// <summary>
/// Strategy cho môn GDKT&PL.
/// Đặc thù: Nhận dạng tình huống pháp luật, bối cảnh kinh tế.
/// </summary>
public sealed class GdktplStrategy : BaseSubjectStrategy
{
    public override SubjectCode SubjectCode => SubjectCode.GDKTPL;

    public override string BuildExtractionPrompt(string documentContent)
    {
        return $"""
        Bạn là chuyên gia bóc tách đề thi Giáo dục Kinh tế và Pháp luật (GDKT&PL) THPT Việt Nam.
        Hãy phân tích đề thi sau và trích xuất TOÀN BỘ, ĐẦY ĐỦ TẤT CẢ câu hỏi theo cấu trúc JSON được chỉ định.
        TUYỆT ĐỐI KHÔNG BỎ SÓT BẤT KỲ CÂU NÀO dù đề thi có 28, 40 hay 50+ câu hỏi.

        QUY TẮC BÓC TÁCH ĐỀ GDKT&PL:
        1. Phần I: Trắc nghiệm 4 lựa chọn (format: "MULTIPLE_CHOICE"), thường là tình huống pháp luật hoặc khái niệm KT
        2. Phần II: Đúng/Sai về tình huống, hành vi đúng/sai theo pháp luật (format: "TRUE_FALSE")
        3. Phần III: Bài toán kinh tế ngắn hoặc tình huống phức tạp (format: "SHORT_ANSWER")
        
        ĐẶC BIỆT LƯU Ý:
        - Bóc tách đầy đủ tất cả câu hỏi từ đầu đến cuối văn bản, không tóm tắt hay dừng giữa chừng.
        - Giữ nguyên tên nhân vật (A, B, anh/chị X...) trong câu hỏi tình huống
        - Phân loại chuyên đề: "Kinh tế vi mô cơ bản", "Kinh tế vĩ mô cơ bản", "Tài chính - Ngân hàng",
          "Pháp luật Hình sự", "Pháp luật Dân sự", "Pháp luật Hành chính", "Hiến pháp và quyền công dân",
          "Pháp luật về lao động", "Tình huống pháp luật thực tiễn"
        - Với câu Đúng/Sai về pháp luật: Ghi rõ điều luật liên quan trong explanation
        - Với câu tính toán kinh tế: Ghi rõ công thức tính và các bước giải

        NỘI DUNG ĐỀ THI:
        {documentContent}

        Trả về JSON array, mỗi phần tử là một câu hỏi đúng theo schema đã cho.
        """;
    }
}
