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

    [Fact]
    public void Extract_WithBrowserOcrFormatAndPageBreaks_CleansPageMarkersAndExtractsAccurately()
    {
        // Arrange
        var ocrRawContent = """
            [Tài liệu: Đề thi thử THPT Quốc gia môn Địa lý 2025]
            [Trình duyệt OCR: Tesseract.js (ngôn ngữ: vie)]

            --- TRANG 1 ---
            PHẦN I. Câu trắc nghiệm nhiều phương án lựa chọn.
            Câu 1: Thủ đô của nước Cộng hòa Xã hội Chủ nghĩa Việt Nam là
            A. Hà Nội.
            B. Đà Nẵng.
            C. Thành phố Hồ Chí Minh.
            D. Cần Thơ.

            --- TRANG 2 ---
            Câu 2: Dãy núi nào sau đây là ranh giới tự nhiên giữa miền Bắc và miền Nam nước ta?
            A. Hoàng Liên Sơn.
            B. Trường Sơn Bắc.
            C. Bạch Mã.
            D. Hoành Sơn.
            """;

        // Act
        var questions = RuleBasedQuestionExtractor.Extract(ocrRawContent, SubjectCode.GEO);

        // Assert
        Assert.Equal(2, questions.Count);

        var q1 = questions[0];
        Assert.Contains("Thủ đô của nước", q1.Content);
        Assert.DoesNotContain("TRANG", q1.Content);
        Assert.NotNull(q1.AnswerData.Options);
        Assert.Equal("Cần Thơ.", q1.AnswerData.Options["D"]);
        Assert.DoesNotContain("TRANG", q1.AnswerData.Options["D"]);

        var q2 = questions[1];
        Assert.Contains("ranh giới tự nhiên", q2.Content);
        Assert.DoesNotContain("TRANG", q2.Content);
        Assert.NotNull(q2.AnswerData.Options);
        Assert.Equal("Hoành Sơn.", q2.AnswerData.Options["D"]);
        Assert.DoesNotContain("TRANG", q2.AnswerData.Options["D"]);
    }

    [Fact]
    public void Extract_WithRealExamInlineAndTwoByTwoOptions_ExtractsAllFourOptionsCorrectly()
    {
        // Giả lập nội dung OCR từ ảnh chụp đề thi Bài 7: Dân số Việt Nam
        var rawContent = """
            BÀI 7: DÂN SỐ VIỆT NAM
            I. TRẮC NGHIỆM NHIỀU LỰA CHỌN
            1. Nhận biết
            Câu 1: Phát biểu nào sau đây đúng về mật độ dân số nước ta?
            A. Ngày càng giảm.        B. Ngày càng tăng.        C. Ít biến động.        D. Mật độ thấp.
            Câu 2: Tỉ trọng nhóm tuổi từ 65 trở lên ở nước ta có xu hướng tăng là do
            A. tuổi thọ trung bình thấp.           B. hệ quả của tăng dân số.
            C. tỉ lệ gia tăng dân số giảm.         D. mức sống được nâng cao.
            Câu 3: Gia tăng dân số trung bình ở nước ta cao nhất vào thời kì nào sau đây?
            A. Từ 1943 đến 1954.    B. Từ 1954 đến 1960.
            C. Từ 1960 đến 1970.    D. Từ 1970 đến 1975.
            """;

        var questions = RuleBasedQuestionExtractor.Extract(rawContent, SubjectCode.GEO);

        Assert.Equal(3, questions.Count);

        // Câu 1: 4 options trên 1 dòng
        var q1 = questions[0];
        Assert.Contains("mật độ dân số nước ta", q1.Content);
        Assert.NotNull(q1.AnswerData.Options);
        Assert.Equal(4, q1.AnswerData.Options.Count);
        Assert.Equal("Ngày càng giảm.", q1.AnswerData.Options["A"]);
        Assert.Equal("Ngày càng tăng.", q1.AnswerData.Options["B"]);
        Assert.Equal("Ít biến động.", q1.AnswerData.Options["C"]);
        Assert.Equal("Mật độ thấp.", q1.AnswerData.Options["D"]);

        // Câu 2: 2 hàng x 2 cột
        var q2 = questions[1];
        Assert.Contains("Tỉ trọng nhóm tuổi từ 65 trở lên", q2.Content);
        Assert.NotNull(q2.AnswerData.Options);
        Assert.Equal(4, q2.AnswerData.Options.Count);
        Assert.Equal("tuổi thọ trung bình thấp.", q2.AnswerData.Options["A"]);
        Assert.Equal("hệ quả của tăng dân số.", q2.AnswerData.Options["B"]);
        Assert.Equal("tỉ lệ gia tăng dân số giảm.", q2.AnswerData.Options["C"]);
        Assert.Equal("mức sống được nâng cao.", q2.AnswerData.Options["D"]);

        // Câu 3: 2 hàng x 2 cột
        var q3 = questions[2];
        Assert.NotNull(q3.AnswerData.Options);
        Assert.Equal(4, q3.AnswerData.Options.Count);
        Assert.Equal("Từ 1943 đến 1954.", q3.AnswerData.Options["A"]);
        Assert.Equal("Từ 1954 đến 1960.", q3.AnswerData.Options["B"]);
        Assert.Equal("Từ 1960 đến 1970.", q3.AnswerData.Options["C"]);
        Assert.Equal("Từ 1970 đến 1975.", q3.AnswerData.Options["D"]);
    }
}

