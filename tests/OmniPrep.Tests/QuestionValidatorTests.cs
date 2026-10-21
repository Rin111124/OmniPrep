using OmniPrep.Domain.Enums;
using OmniPrep.Domain.ValueObjects;
using OmniPrep.Infrastructure.Validation;
using Xunit;

namespace OmniPrep.Tests;

public class QuestionValidatorTests
{
    private readonly QuestionValidator _validator = new();

    [Fact]
    public void Validate_ValidMultipleChoice_ReturnsValid()
    {
        var q = new ExtractedQuestion
        {
            Content = "Thủ đô của Việt Nam là gì?",
            Topic = "Địa lý Việt Nam",
            Difficulty = "Recognition",
            Format = "MULTIPLE_CHOICE",
            AnswerData = new AnswerData
            {
                Options = new Dictionary<string, string>
                {
                    ["A"] = "Hà Nội",
                    ["B"] = "Hồ Chí Minh",
                    ["C"] = "Đà Nẵng",
                    ["D"] = "Hải Phòng"
                },
                CorrectOption = "A"
            },
            Explanation = "Hà Nội là thủ đô của nước CHXHCN Việt Nam."
        };

        var result = _validator.Validate(q, SubjectCode.GEO);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_MultipleChoice_MissingOption_ReturnsInvalid()
    {
        var q = new ExtractedQuestion
        {
            Content = "Thủ đô của Việt Nam là gì?",
            Topic = "Địa lý",
            Difficulty = "Recognition",
            Format = "MULTIPLE_CHOICE",
            AnswerData = new AnswerData
            {
                Options = new Dictionary<string, string>
                {
                    ["A"] = "Hà Nội",
                    ["B"] = "Hồ Chí Minh",
                    ["C"] = "Đà Nẵng"
                    // Missing D
                },
                CorrectOption = "A"
            }
        };

        var result = _validator.Validate(q, SubjectCode.GEO);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("4 phương án"));
    }

    [Fact]
    public void Validate_MultipleChoice_InvalidCorrectOption_ReturnsInvalid()
    {
        var q = new ExtractedQuestion
        {
            Content = "Câu hỏi trắc nghiệm?",
            Topic = "Địa lý",
            Difficulty = "Recognition",
            Format = "MULTIPLE_CHOICE",
            AnswerData = new AnswerData
            {
                Options = new Dictionary<string, string>
                {
                    ["A"] = "A",
                    ["B"] = "B",
                    ["C"] = "C",
                    ["D"] = "D"
                },
                CorrectOption = "E"
            }
        };

        var result = _validator.Validate(q, SubjectCode.GEO);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("không nằm trong danh sách"));
    }

    [Fact]
    public void Validate_ValidTrueFalse_ReturnsValid()
    {
        var q = new ExtractedQuestion
        {
            Content = "Cho các nhận định sau về khí hậu Việt Nam:",
            Topic = "Khí hậu",
            Difficulty = "Comprehension",
            Format = "TRUE_FALSE",
            AnswerData = new AnswerData
            {
                Statements = new List<TrueFalseStatement>
                {
                    new() { Id = "a", Text = "Việt Nam có khí hậu nhiệt đới gió mùa.", IsCorrect = true },
                    new() { Id = "b", Text = "Miền Bắc có mùa đông lạnh.", IsCorrect = true },
                    new() { Id = "c", Text = "Miền Nam có mùa đông buốt tuyết rơi.", IsCorrect = false },
                    new() { Id = "d", Text = "Lượng mưa phân bố đồng đều quanh năm.", IsCorrect = false }
                }
            }
        };

        var result = _validator.Validate(q, SubjectCode.GEO);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_TrueFalse_WithOnlyThreeStatements_ReturnsInvalid()
    {
        var q = new ExtractedQuestion
        {
            Content = "Cho các nhận định:",
            Topic = "Khí hậu",
            Difficulty = "Comprehension",
            Format = "TRUE_FALSE",
            AnswerData = new AnswerData
            {
                Statements = new List<TrueFalseStatement>
                {
                    new() { Id = "a", Text = "Ý 1", IsCorrect = true },
                    new() { Id = "b", Text = "Ý 2", IsCorrect = true },
                    new() { Id = "c", Text = "Ý 3", IsCorrect = false }
                }
            }
        };

        var result = _validator.Validate(q, SubjectCode.GEO);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("đúng 4 mệnh đề"));
    }

    [Fact]
    public void Validate_ValidShortAnswer_ReturnsValid()
    {
        var q = new ExtractedQuestion
        {
            Content = "Tính mật độ dân số vùng Đồng bằng sông Hồng năm 2021 (người/km2):",
            Topic = "Dân cư",
            Difficulty = "Application",
            Format = "SHORT_ANSWER",
            AnswerData = new AnswerData
            {
                NumericValue = 1060m,
                Tolerance = 0.5m
            }
        };

        var result = _validator.Validate(q, SubjectCode.GEO);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_ShortAnswer_MissingValue_ReturnsInvalid()
    {
        var q = new ExtractedQuestion
        {
            Content = "Tính mật độ dân số:",
            Topic = "Dân cư",
            Difficulty = "Application",
            Format = "SHORT_ANSWER",
            AnswerData = new AnswerData()
        };

        var result = _validator.Validate(q, SubjectCode.GEO);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("SHORT_ANSWER"));
    }

    [Fact]
    public void Validate_EmptyContent_ReturnsInvalid()
    {
        var q = new ExtractedQuestion
        {
            Content = "",
            Topic = "Dân cư",
            Difficulty = "Application",
            Format = "SHORT_ANSWER",
            AnswerData = new AnswerData
            {
                NumericValue = 100m
            }
        };

        var result = _validator.Validate(q, SubjectCode.GEO);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("quá ngắn hoặc bị rỗng"));
    }
}
