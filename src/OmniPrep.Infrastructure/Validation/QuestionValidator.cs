using OmniPrep.Domain.Enums;
using OmniPrep.Domain.Interfaces;
using OmniPrep.Domain.ValueObjects;

namespace OmniPrep.Infrastructure.Validation;

/// <summary>
/// Trạm kiểm tra chất lượng & schema độc lập cho câu hỏi trích xuất (Mục 6.2 Lộ trình).
/// Chặn các câu hỏi sai format, thiếu đáp án hoặc rỗng trước khi đưa vào ngân hàng đề.
/// </summary>
public sealed class QuestionValidator : IQuestionValidator
{
    private static readonly HashSet<string> ValidDifficulties = new(StringComparer.OrdinalIgnoreCase)
    {
        "NHAN_BIET", "THONG_HIEU", "VAN_DUNG", "VAN_DUNG_CAO",
        "NhanBiet", "ThongHieu", "VanDung", "VanDungCao"
    };

    public QuestionValidationResult Validate(ExtractedQuestion question, SubjectCode subjectCode)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // 1. Kiểm tra nội dung câu hỏi
        if (string.IsNullOrWhiteSpace(question.Content) || question.Content.Trim().Length < 5)
        {
            errors.Add("Nội dung câu hỏi quá ngắn hoặc bị rỗng.");
        }

        // 2. Kiểm tra chuyên đề (Topic)
        if (string.IsNullOrWhiteSpace(question.Topic))
        {
            errors.Add("Thiếu chuyên đề (Topic) của câu hỏi.");
        }

        // 3. Kiểm tra độ khó
        if (string.IsNullOrWhiteSpace(question.Difficulty) || !ValidDifficulties.Contains(question.Difficulty.Trim()))
        {
            warnings.Add($"Độ khó '{question.Difficulty}' chưa chuẩn hóa, sẽ được gán mặc định.");
        }

        // 4. Kiểm tra format và dữ liệu đáp án tương ứng
        var format = question.Format?.Trim().ToUpperInvariant() ?? string.Empty;
        var data = question.AnswerData ?? new AnswerData();

        switch (format)
        {
            case "MULTIPLE_CHOICE":
                ValidateMultipleChoice(data, errors, warnings);
                break;

            case "TRUE_FALSE":
                ValidateTrueFalse(data, errors, warnings);
                break;

            case "SHORT_ANSWER":
                ValidateShortAnswer(data, errors, warnings);
                break;

            default:
                errors.Add($"Định dạng câu hỏi không hợp lệ: '{question.Format}'. Chấp nhận: MULTIPLE_CHOICE, TRUE_FALSE, SHORT_ANSWER.");
                break;
        }

        // 5. Kiểm tra lời giải chi tiết
        if (string.IsNullOrWhiteSpace(question.Explanation))
        {
            warnings.Add("Câu hỏi chưa có lời giải chi tiết (Explanation).");
        }

        return new QuestionValidationResult(
            IsValid: errors.Count == 0,
            Errors: errors,
            Warnings: warnings
        );
    }

    private static void ValidateMultipleChoice(AnswerData data, List<string> errors, List<string> warnings)
    {
        if (data.Options is null || data.Options.Count < 4)
        {
            errors.Add($"Câu hỏi trắc nghiệm (MULTIPLE_CHOICE) phải có đủ 4 phương án A, B, C, D (hiện có: {data.Options?.Count ?? 0}).");
            return;
        }

        if (string.IsNullOrWhiteSpace(data.CorrectOption))
        {
            errors.Add("Câu hỏi trắc nghiệm thiếu đáp án đúng (correctOption).");
            return;
        }

        var cleanCorrect = data.CorrectOption.Trim().ToUpperInvariant();
        if (!data.Options.Keys.Any(k => string.Equals(k.Trim(), cleanCorrect, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add($"Đáp án đúng '{cleanCorrect}' không nằm trong danh sách các phương án ({string.Join(", ", data.Options.Keys)}).");
        }
    }

    private static void ValidateTrueFalse(AnswerData data, List<string> errors, List<string> warnings)
    {
        if (data.Statements is null || data.Statements.Count != 4)
        {
            errors.Add($"Câu hỏi Đúng/Sai (TRUE_FALSE) phải có đúng 4 mệnh đề con a, b, c, d (hiện có: {data.Statements?.Count ?? 0}).");
            return;
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var stmt in data.Statements)
        {
            if (string.IsNullOrWhiteSpace(stmt.Text))
            {
                errors.Add($"Mệnh đề '{stmt.Id}' bị rỗng nội dung.");
            }
            if (!ids.Add(stmt.Id))
            {
                errors.Add($"Trùng lặp mã mệnh đề: '{stmt.Id}'.");
            }
        }
    }

    private static void ValidateShortAnswer(AnswerData data, List<string> errors, List<string> warnings)
    {
        var hasNumeric = data.NumericValue.HasValue;
        var hasText = data.AcceptedTexts is not null && data.AcceptedTexts.Any(t => !string.IsNullOrWhiteSpace(t));

        if (!hasNumeric && !hasText)
        {
            errors.Add("Câu hỏi trả lời ngắn (SHORT_ANSWER) phải có đáp án số (NumericValue) hoặc tập đáp án chữ (AcceptedTexts).");
        }

        if (data.Tolerance.HasValue && data.Tolerance.Value < 0)
        {
            errors.Add("Dung sai (Tolerance) của đáp án số không được âm.");
        }
    }
}
