using System.Text.RegularExpressions;
using OmniPrep.Domain.Enums;
using OmniPrep.Infrastructure.Services;
using Xunit;

namespace OmniPrep.Tests;

/// <summary>
/// Kiểm thử toàn diện tính năng:
/// 1. Tự động nhận diện nguồn đề, năm thi, môn học (Auto Source & Metadata Detection)
/// 2. Danh mục đề thi mẫu tích hợp sẵn (OnlineExamCatalog)
/// 3. Provider tìm kiếm đề trực tuyến có kiểm soát (CuratedSourcesDiscoveryProvider)
/// 4. Khả năng bóc tách tự động chỉ với 1 chạm (Rule-based / Auto Ingestion)
/// </summary>
public class ExamDiscoveryAndAutoIngestTests
{
    // =========================================================================
    // 1. KIỂM THỬ REGEX TỰ ĐỘNG NHẬN DIỆN NGUỒN, NĂM, MÔN (ONE-TOUCH SCAN DETECTION)
    // =========================================================================

    [Theory]
    [InlineData("SỞ GIÁO DỤC VÀ ĐÀO TẠO HÀ NỘI\nKỲ THI THỬ TỐT NGHIỆP THPT 2025", "Sở GD&ĐT Hà Nội")]
    [InlineData("SỞ GD&ĐT BẮC NINH\nTRƯỜNG THPT CHUYÊN BẮC NINH", "Sở GD&ĐT Bắc Ninh")]
    [InlineData("SỞ GIÁO DỤC & ĐÀO TẠO NAM ĐỊNH\nĐỀ THI KHẢO SÁT CHẤT LƯỢNG", "Sở GD&ĐT Nam Định")]
    [InlineData("SỞ GD & ĐT NGHỆ AN\nKỲ THI HỌC SINH GIỎI", "Sở GD&ĐT Nghệ An")]
    [InlineData("BỘ GIÁO DỤC VÀ ĐÀO TẠO\nĐỀ THI THAM KHẢO 2025", "Bộ Giáo Dục & Đào Tạo")]
    [InlineData("TRƯỜNG THPT CHUYÊN LAM SƠN\nĐỀ THI THỬ LẦN 1", "THPT Lam Sơn")]
    public void AutoDetectSource_ExtractsCorrectInstitution(string text, string expectedPrefix)
    {
        string detectedSource = "";

        if (Regex.IsMatch(text, @"BỘ\s+GIÁO\s+DỤC\s+(?:VÀ|&)\s+ĐÀO\s+TẠO", RegexOptions.IgnoreCase))
        {
            detectedSource = "Bộ Giáo Dục & Đào Tạo";
        }
        else
        {
            var soMatch = Regex.Match(text, @"SỞ\s+(?:GIÁO\s+DỤC\s+(?:VÀ|&)\s+ĐÀO\s+TẠO|GD\s*&?\s*ĐT)\s+([A-ZÀ-Ỹ\s]+?)(?:\s+TRƯỜNG|\s+ĐỀ|\s+KỲ|\s*\n|$)", RegexOptions.IgnoreCase);
            if (soMatch.Success)
            {
                var prov = soMatch.Groups[1].Value.Trim().Replace("-", " ");
                detectedSource = $"Sở GD&ĐT {char.ToUpper(prov[0]) + prov.Substring(1).ToLower()}";
            }
            else
            {
                var thptMatch = Regex.Match(text, @"TRƯỜNG\s+THPT\s+(?:CHUYÊN\s+)?([A-ZÀ-Ỹ\s]+?)(?:\s+ĐỀ|\s+KỲ|\s*\n|$)", RegexOptions.IgnoreCase);
                if (thptMatch.Success)
                {
                    var school = thptMatch.Groups[1].Value.Trim();
                    detectedSource = $"THPT {char.ToUpper(school[0]) + school.Substring(1).ToLower()}";
                }
            }
        }

        Assert.Contains(expectedPrefix, detectedSource, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("KỲ THI TỐT NGHIỆP TRUNG HỌC PHỔ THÔNG NĂM 2025", 2025)]
    [InlineData("NĂM HỌC 2024-2025\nĐỀ THI KHẢO SÁT", 2025)]
    [InlineData("ĐỀ THI TUYỂN SINH NĂM 2024", 2024)]
    [InlineData("KHÓA THI 2026", 2026)]
    public void AutoDetectYear_ExtractsCorrectYear(string text, int expectedYear)
    {
        var yearMatch = Regex.Match(text, @"(?:năm\s+học|năm|khóa)\s*(?:20\d\d[-–])?\s*(202\d)", RegexOptions.IgnoreCase);
        if (!yearMatch.Success)
        {
            yearMatch = Regex.Match(text, @"\b(202\d)\b");
        }
        Assert.True(yearMatch.Success);
        Assert.Equal(expectedYear, int.Parse(yearMatch.Groups[1].Value));
    }

    [Theory]
    [InlineData("BÀI THI: ĐỊA LÍ\nThời gian làm bài: 50 phút", SubjectCode.GEO)]
    [InlineData("MÔN: LỊCH SỬ\nKỳ thi tốt nghiệp THPT", SubjectCode.HIS)]
    [InlineData("BÀI THI: TOÁN HỌC\nMã đề 101", SubjectCode.MATH)]
    [InlineData("MÔN VẬT LÍ\nThời gian 50 phút", SubjectCode.PHYS)]
    [InlineData("BÀI THI KHOA HỌC: HÓA HỌC", SubjectCode.CHEM)]
    [InlineData("MÔN: SINH HỌC\nPhần trắc nghiệm", SubjectCode.BIO)]
    [InlineData("MÔN TIẾNG ANH\nTest 1", SubjectCode.ENG)]
    [InlineData("GIÁO DỤC KINH TẾ VÀ PHÁP LUẬT", SubjectCode.GDKTPL)]
    public void AutoDetectSubject_IdentifiesCorrectSubject(string text, SubjectCode expectedCode)
    {
        SubjectCode? detected = null;

        if (Regex.IsMatch(text, @"ĐỊA\s+L[ÍÝ]", RegexOptions.IgnoreCase)) detected = SubjectCode.GEO;
        else if (Regex.IsMatch(text, @"LỊCH\s+SỬ", RegexOptions.IgnoreCase)) detected = SubjectCode.HIS;
        else if (Regex.IsMatch(text, @"TOÁN", RegexOptions.IgnoreCase)) detected = SubjectCode.MATH;
        else if (Regex.IsMatch(text, @"VẬT\s+L[ÍÝ]", RegexOptions.IgnoreCase)) detected = SubjectCode.PHYS;
        else if (Regex.IsMatch(text, @"HÓA\s+HỌC", RegexOptions.IgnoreCase)) detected = SubjectCode.CHEM;
        else if (Regex.IsMatch(text, @"SINH\s+HỌC", RegexOptions.IgnoreCase)) detected = SubjectCode.BIO;
        else if (Regex.IsMatch(text, @"TIẾNG\s+ANH", RegexOptions.IgnoreCase)) detected = SubjectCode.ENG;
        else if (Regex.IsMatch(text, @"KINH\s+TẾ|PHÁP\s+LUẬT", RegexOptions.IgnoreCase)) detected = SubjectCode.GDKTPL;

        Assert.Equal(expectedCode, detected);
    }

    // =========================================================================
    // 2. KIỂM THỬ KHO ĐỀ MẪU TÍCH HỢP (ONLINE EXAM CATALOG - ONE TOUCH AUTO-FETCH)
    // =========================================================================

    [Fact]
    public void OnlineExamCatalog_GetCatalog_ReturnsValidItemsForKeySubjects()
    {
        var items = OnlineExamCatalog.GetCatalog();

        Assert.NotNull(items);
        Assert.NotEmpty(items);

        // Đảm bảo có đề thi chuẩn cấu trúc 2025 cho các môn chính
        Assert.Contains(items, i => i.SubjectCode == SubjectCode.GEO && i.ExamYear == 2025);
        Assert.Contains(items, i => i.SubjectCode == SubjectCode.MATH && i.ExamYear == 2025);
        Assert.Contains(items, i => i.SubjectCode == SubjectCode.HIS && i.ExamYear == 2025);

        // Kiểm tra tính toàn vẹn của từng đề trong Catalog
        foreach (var item in items)
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Id), "Id không được rỗng");
            Assert.False(string.IsNullOrWhiteSpace(item.Title), "Title không được rỗng");
            Assert.False(string.IsNullOrWhiteSpace(item.SourceName), "SourceName không được rỗng");
            Assert.False(string.IsNullOrWhiteSpace(item.RemoteUrl), "RemoteUrl không được rỗng");
            Assert.True(item.ExamYear >= 2024, "Năm thi phải >= 2024");
            Assert.True(item.TotalQuestionsEstimated > 0, "Số lượng câu hỏi ước tính phải > 0");

            // Nội dung thô để bóc tách một chạm phải đầy đủ
            var content = item.GetContent();
            Assert.False(string.IsNullOrWhiteSpace(content), $"Nội dung đề mẫu '{item.Title}' không được rỗng");
            Assert.Contains("Câu", content);
        }
    }

    [Theory]
    [InlineData("bgd-geo-2025", SubjectCode.GEO)]
    [InlineData("bgd-math-2025", SubjectCode.MATH)]
    [InlineData("bgd-hist-2025", SubjectCode.HIS)]
    public void OnlineExamCatalog_SampleContent_ExtractsQuestionsSuccessfully(string catalogId, SubjectCode subject)
    {
        var item = OnlineExamCatalog.GetCatalog().FirstOrDefault(c => c.Id == catalogId);
        Assert.NotNull(item);

        var content = item.GetContent();
        var questions = RuleBasedQuestionExtractor.Extract(content, subject);

        // Kiểm tra việc bóc tách một chạm không bị lỗi và trích xuất ra câu hỏi
        Assert.NotEmpty(questions);
        Assert.All(questions, q =>
        {
            Assert.False(string.IsNullOrWhiteSpace(q.Content), "Nội dung câu hỏi không được rỗng");
            Assert.False(string.IsNullOrWhiteSpace(q.Format), "Định dạng câu hỏi không được rỗng");
        });

        // Đảm bảo có ít nhất 8 câu hỏi được bóc tách từ đề thi mẫu chuẩn
        Assert.True(questions.Count >= 8, $"Đề {catalogId} cần bóc tách ít nhất 8 câu hỏi, thực tế: {questions.Count}");
    }

    // =========================================================================
    // 3. KIỂM THỬ DISCOVERY PROVIDER (TÌM NGUỒN CÓ PHÊ DUYỆT - PROVENANCE)
    // =========================================================================

    [Fact]
    public async Task CuratedSourcesDiscoveryProvider_DiscoverAll_ReturnsRichMetadata()
    {
        var provider = new CuratedSourcesDiscoveryProvider();
        var candidates = await provider.DiscoverAsync();

        Assert.NotEmpty(candidates);

        foreach (var c in candidates)
        {
            // Kiểm tra các trường bắt buộc theo thiết kế Provenance P1
            Assert.False(string.IsNullOrWhiteSpace(c.Id));
            Assert.False(string.IsNullOrWhiteSpace(c.Title));
            Assert.False(string.IsNullOrWhiteSpace(c.SourceName));
            Assert.False(string.IsNullOrWhiteSpace(c.SourcePageUrl), "Phải có URL trang nguồn đối chiếu");
            Assert.False(string.IsNullOrWhiteSpace(c.DownloadUrl), "Phải có URL tải tài liệu");
            Assert.False(string.IsNullOrWhiteSpace(c.PublishedDate), "Phải có ngày công bố nguồn");
            Assert.False(string.IsNullOrWhiteSpace(c.CuratedReason), "Phải có lý do nguồn được phê duyệt");
            Assert.True(c.DownloadUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase), "URL tải phải là HTTPS");
            Assert.True(c.SourcePageUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase), "URL trang nguồn phải là HTTPS");
        }
    }

    [Theory]
    [InlineData(SubjectCode.GEO)]
    [InlineData(SubjectCode.MATH)]
    [InlineData(SubjectCode.PHYS)]
    [InlineData(SubjectCode.CHEM)]
    [InlineData(SubjectCode.HIS)]
    [InlineData(SubjectCode.ENG)]
    [InlineData(SubjectCode.GDKTPL)]
    public async Task CuratedSourcesDiscoveryProvider_FilterBySubject_ReturnsAccurateCandidates(SubjectCode code)
    {
        var provider = new CuratedSourcesDiscoveryProvider();
        var candidates = await provider.DiscoverAsync(subjectCode: code);

        Assert.NotEmpty(candidates);
        Assert.All(candidates, c => Assert.Equal(code, c.SubjectCode));
    }

    [Fact]
    public async Task CuratedSourcesDiscoveryProvider_FilterByYear_ReturnsMatchingYearOnly()
    {
        var provider = new CuratedSourcesDiscoveryProvider();
        var candidates = await provider.DiscoverAsync(year: 2025);

        Assert.NotEmpty(candidates);
        Assert.All(candidates, c => Assert.Equal(2025, c.ExamYear));
    }
}
