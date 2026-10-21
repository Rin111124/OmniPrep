using OmniPrep.Domain.Enums;
using OmniPrep.Infrastructure.Services;
using Xunit;

namespace OmniPrep.Tests;

public class RuleBasedQuestionExtractorTests
{
    [Fact]
    public void Extract_WithStandardSamplePaper_ExtractsAllThreeParts()
    {
        // Arrange
        var catalog = OnlineExamCatalog.GetCatalog();
        var geoExam = catalog.First(c => c.SubjectCode == SubjectCode.GEO);
        var rawContent = geoExam.GetContent();

        // Act
        var questions = RuleBasedQuestionExtractor.Extract(rawContent, SubjectCode.GEO);

        // Assert
        Assert.NotEmpty(questions);
        Assert.True(questions.Count >= 20, $"Expected >= 20 questions, got {questions.Count}");

        var part1 = questions.Where(q => q.Format == "MULTIPLE_CHOICE").ToList();
        var part2 = questions.Where(q => q.Format == "TRUE_FALSE").ToList();
        var part3 = questions.Where(q => q.Format == "SHORT_ANSWER").ToList();

        Assert.True(part1.Count >= 10, "Part 1 should have multiple choice questions");
        Assert.True(part2.Count >= 2, "Part 2 should have true/false questions");
        Assert.True(part3.Count >= 2, "Part 3 should have short answer questions");

        // Validate Part 1 structure
        foreach (var q in part1)
        {
            Assert.NotNull(q.AnswerData.Options);
            Assert.True(q.AnswerData.Options.Count >= 4);
            Assert.False(string.IsNullOrWhiteSpace(q.AnswerData.CorrectOption));
        }

        // Validate Part 2 structure
        foreach (var q in part2)
        {
            Assert.NotNull(q.AnswerData.Statements);
            Assert.Equal(4, q.AnswerData.Statements.Count);
        }

        // Validate Part 3 structure
        foreach (var q in part3)
        {
            Assert.True(q.AnswerData.NumericValue.HasValue || (q.AnswerData.AcceptedTexts != null && q.AnswerData.AcceptedTexts.Count > 0));
        }
    }

    [Fact]
    public void Extract_WithEmptyContent_ReturnsEmptyList()
    {
        var questions = RuleBasedQuestionExtractor.Extract("", SubjectCode.GEO);
        Assert.Empty(questions);
    }
}
