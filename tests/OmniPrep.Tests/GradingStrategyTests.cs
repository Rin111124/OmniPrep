using OmniPrep.Domain.Enums;
using OmniPrep.Domain.ValueObjects;
using OmniPrep.Infrastructure.Services;
using Xunit;

namespace OmniPrep.Tests;

public class GradingStrategyTests
{
    private readonly GeographyStrategy _geoStrategy = new();

    [Theory]
    [InlineData("A", "A", 0.25)]
    [InlineData("A", "a", 0.25)]
    [InlineData("B", " B ", 0.25)]
    [InlineData("C", "D", 0.0)]
    [InlineData("A", "", 0.0)]
    [InlineData("A", null, 0.0)]
    public void GradeMultipleChoice_EvaluatesCorrectly(string correctAnswer, string? userAnswer, decimal expectedScore)
    {
        var score = _geoStrategy.GradeMultipleChoice(correctAnswer, userAnswer);
        Assert.Equal(expectedScore, score);
    }

    [Fact]
    public void GradeTrueFalse_FourCorrect_AwardsOneFullPoint()
    {
        var statements = new List<TrueFalseStatement>
        {
            new() { Id = "a", Text = "Statement 1", IsCorrect = true },
            new() { Id = "b", Text = "Statement 2", IsCorrect = false },
            new() { Id = "c", Text = "Statement 3", IsCorrect = true },
            new() { Id = "d", Text = "Statement 4", IsCorrect = false }
        };

        var userAnswers = new Dictionary<string, bool>
        {
            ["a"] = true,
            ["b"] = false,
            ["c"] = true,
            ["d"] = false
        };

        var score = _geoStrategy.GradeTrueFalse(statements, userAnswers);
        Assert.Equal(1.00m, score);
    }

    [Fact]
    public void GradeTrueFalse_ThreeCorrect_AwardsPointFive()
    {
        var statements = new List<TrueFalseStatement>
        {
            new() { Id = "a", Text = "Statement 1", IsCorrect = true },
            new() { Id = "b", Text = "Statement 2", IsCorrect = false },
            new() { Id = "c", Text = "Statement 3", IsCorrect = true },
            new() { Id = "d", Text = "Statement 4", IsCorrect = false }
        };

        // 3 correct, 1 wrong
        var userAnswers = new Dictionary<string, bool>
        {
            ["a"] = true,
            ["b"] = false,
            ["c"] = true,
            ["d"] = true // wrong
        };

        var score = _geoStrategy.GradeTrueFalse(statements, userAnswers);
        Assert.Equal(0.50m, score);
    }

    [Fact]
    public void GradeTrueFalse_TwoCorrect_AwardsPointTwentyFive()
    {
        var statements = new List<TrueFalseStatement>
        {
            new() { Id = "a", Text = "Statement 1", IsCorrect = true },
            new() { Id = "b", Text = "Statement 2", IsCorrect = false },
            new() { Id = "c", Text = "Statement 3", IsCorrect = true },
            new() { Id = "d", Text = "Statement 4", IsCorrect = false }
        };

        var userAnswers = new Dictionary<string, bool>
        {
            ["a"] = true,
            ["b"] = true, // wrong
            ["c"] = false, // wrong
            ["d"] = false
        };

        var score = _geoStrategy.GradeTrueFalse(statements, userAnswers);
        Assert.Equal(0.25m, score);
    }

    [Fact]
    public void GradeTrueFalse_OneCorrect_AwardsPointTen()
    {
        var statements = new List<TrueFalseStatement>
        {
            new() { Id = "a", Text = "Statement 1", IsCorrect = true },
            new() { Id = "b", Text = "Statement 2", IsCorrect = false },
            new() { Id = "c", Text = "Statement 3", IsCorrect = true },
            new() { Id = "d", Text = "Statement 4", IsCorrect = false }
        };

        var userAnswers = new Dictionary<string, bool>
        {
            ["a"] = true,
            ["b"] = true, // wrong
            ["c"] = false, // wrong
            ["d"] = true // wrong
        };

        var score = _geoStrategy.GradeTrueFalse(statements, userAnswers);
        Assert.Equal(0.10m, score);
    }

    [Fact]
    public void GradeTrueFalse_ZeroCorrectOrNull_AwardsZero()
    {
        var statements = new List<TrueFalseStatement>
        {
            new() { Id = "a", Text = "Statement 1", IsCorrect = true },
            new() { Id = "b", Text = "Statement 2", IsCorrect = false }
        };

        Assert.Equal(0.00m, _geoStrategy.GradeTrueFalse(statements, null));
        Assert.Equal(0.00m, _geoStrategy.GradeTrueFalse(statements, new Dictionary<string, bool>()));
    }

    [Theory]
    [InlineData("1234.56", 1234.56, 0.01, 0.25)]
    [InlineData("1234,56", 1234.56, 0.01, 0.25)]
    [InlineData(" 1234.56 ", 1234.56, 0.01, 0.25)]
    [InlineData("1234.569", 1234.56, 0.01, 0.25)] // within tolerance
    [InlineData("1234.60", 1234.56, 0.01, 0.0)]  // outside tolerance
    [InlineData("invalid", 1234.56, 0.01, 0.0)]
    [InlineData("", 1234.56, 0.01, 0.0)]
    [InlineData(null, 1234.56, 0.01, 0.0)]
    public void GradeShortAnswer_NumericWithTolerance_EvaluatesCorrectly(
        string? userInput,
        double targetValue,
        double tolerance,
        decimal expectedScore)
    {
        var answerData = new AnswerData
        {
            NumericValue = (decimal)targetValue,
            Tolerance = (decimal)tolerance
        };

        var score = _geoStrategy.GradeShortAnswer(answerData, userInput);
        Assert.Equal(expectedScore, score);
    }

    [Theory]
    [InlineData("Hà Nội", 0.25)]
    [InlineData("hà nội", 0.25)]
    [InlineData(" Hà Nội ", 0.25)]
    [InlineData("TP. Hồ Chí Minh", 0.0)]
    [InlineData("", 0.0)]
    [InlineData(null, 0.0)]
    public void GradeShortAnswer_AcceptedTexts_EvaluatesCorrectly(string? userInput, decimal expectedScore)
    {
        var answerData = new AnswerData
        {
            AcceptedTexts = new List<string> { "Hà Nội", "Thủ đô Hà Nội" }
        };

        var score = _geoStrategy.GradeShortAnswer(answerData, userInput);
        Assert.Equal(expectedScore, score);
    }
}
