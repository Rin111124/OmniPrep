using OmniPrep.Domain.Enums;

namespace OmniPrep.Infrastructure.Services;

/// <summary>
/// Đại diện cho một ứng viên đề thi trực tuyến từ nguồn chính thức được phê duyệt (P1 Discovery)
/// </summary>
public sealed record ExamCandidate(
    string Id,
    string Title,
    SubjectCode SubjectCode,
    string SourceName,
    short ExamYear,
    string DocumentType,
    string SourcePageUrl,
    string DownloadUrl,
    string PublishedDate,
    string Description,
    long? FileSizeBytes,
    bool IsAlreadyIngested,
    string CuratedReason
);

/// <summary>
/// Giao diện tìm kiếm đề thi từ các nguồn trực tuyến được phê duyệt (P1 Lộ trình tìm đề)
/// </summary>
public interface IExamDiscoveryProvider
{
    Task<IReadOnlyList<ExamCandidate>> DiscoverAsync(
        SubjectCode? subjectCode = null,
        short? year = null,
        CancellationToken ct = default);
}

/// <summary>
/// Provider tìm kiếm đề thi từ các nguồn giáo dục chính thức (Bộ GD&ĐT, các Sở GD&ĐT) có nguồn gốc rõ ràng
/// </summary>
public sealed class CuratedSourcesDiscoveryProvider : IExamDiscoveryProvider
{
    private static readonly List<ExamCandidate> CuratedCandidates =
    [
        // ==================== ĐỊA LÝ ====================
        new ExamCandidate(
            Id: "curated-geo-moet-2025",
            Title: "Đề tham khảo Tốt nghiệp THPT 2025 - Bộ GD&ĐT (Môn Địa lí)",
            SubjectCode: SubjectCode.GEO,
            SourceName: "Bộ Giáo Dục & Đào Tạo 2025",
            ExamYear: 2025,
            DocumentType: "PDF",
            SourcePageUrl: "https://moet.gov.vn/tintuc/Pages/tin-tong-hop.aspx?ItemID=8932",
            DownloadUrl: "https://moet.gov.vn/content/tintuc/Documents/De-tham-khao-2025/Dia-li-2025.pdf",
            PublishedDate: "2024-10-18",
            Description: "Đề tham khảo chính thức chuẩn cấu trúc GDPT 2018 (28 câu: 18 câu P.I, 4 câu P.II, 6 câu P.III).",
            FileSizeBytes: 1450000,
            IsAlreadyIngested: false,
            CuratedReason: "Nguồn chính thức từ cổng thông tin Bộ GD&ĐT phục vụ kỳ thi Tốt nghiệp THPT 2025."
        ),
        new ExamCandidate(
            Id: "curated-geo-hanoi-2025",
            Title: "Đề thi thử Tốt nghiệp THPT 2025 - Sở GD&ĐT Hà Nội (Môn Địa lí)",
            SubjectCode: SubjectCode.GEO,
            SourceName: "Sở GD&ĐT Hà Nội 2025",
            ExamYear: 2025,
            DocumentType: "PDF",
            SourcePageUrl: "https://hanoi.edu.vn/khao-thi/de-thi-thu-tot-nghiep-thpt-2025",
            DownloadUrl: "https://hanoi.edu.vn/documents/de-thi/2025/dia-li-dot-1.pdf",
            PublishedDate: "2025-03-12",
            Description: "Đề thi khảo sát chất lượng học sinh lớp 12 toàn thành phố Hà Nội lần 1.",
            FileSizeBytes: 1820000,
            IsAlreadyIngested: false,
            CuratedReason: "Đề khảo sát chuẩn cấu trúc quy mô lớn có tính phân hóa cao."
        ),

        // ==================== TOÁN HỌC ====================
        new ExamCandidate(
            Id: "curated-math-moet-2025",
            Title: "Đề tham khảo Tốt nghiệp THPT 2025 - Bộ GD&ĐT (Môn Toán)",
            SubjectCode: SubjectCode.MATH,
            SourceName: "Bộ Giáo Dục & Đào Tạo 2025",
            ExamYear: 2025,
            DocumentType: "PDF",
            SourcePageUrl: "https://moet.gov.vn/tintuc/Pages/tin-tong-hop.aspx?ItemID=8932",
            DownloadUrl: "https://moet.gov.vn/content/tintuc/Documents/De-tham-khao-2025/Toan-2025.pdf",
            PublishedDate: "2024-10-18",
            Description: "Cấu trúc 34 câu (12 câu P.I, 4 câu P.II Đúng/Sai, 6 câu P.III Trả lời ngắn số).",
            FileSizeBytes: 1650000,
            IsAlreadyIngested: false,
            CuratedReason: "Đề tham khảo chính thức chuẩn cấu trúc Bộ GD&ĐT 2025."
        ),

        // ==================== VẬT LÝ ====================
        new ExamCandidate(
            Id: "curated-phys-moet-2025",
            Title: "Đề tham khảo Tốt nghiệp THPT 2025 - Bộ GD&ĐT (Môn Vật lí)",
            SubjectCode: SubjectCode.PHYS,
            SourceName: "Bộ Giáo Dục & Đào Tạo 2025",
            ExamYear: 2025,
            DocumentType: "PDF",
            SourcePageUrl: "https://moet.gov.vn/tintuc/Pages/tin-tong-hop.aspx?ItemID=8932",
            DownloadUrl: "https://moet.gov.vn/content/tintuc/Documents/De-tham-khao-2025/Vat-li-2025.pdf",
            PublishedDate: "2024-10-18",
            Description: "Đề thi gồm 28 câu hỏi trắc nghiệm, đúng sai và trả lời ngắn.",
            FileSizeBytes: 1390000,
            IsAlreadyIngested: false,
            CuratedReason: "Đề tham khảo chính thức Bộ GD&ĐT."
        ),

        // ==================== HÓA HỌC ====================
        new ExamCandidate(
            Id: "curated-chem-moet-2025",
            Title: "Đề tham khảo Tốt nghiệp THPT 2025 - Bộ GD&ĐT (Môn Hóa học)",
            SubjectCode: SubjectCode.CHEM,
            SourceName: "Bộ Giáo Dục & Đào Tạo 2025",
            ExamYear: 2025,
            DocumentType: "PDF",
            SourcePageUrl: "https://moet.gov.vn/tintuc/Pages/tin-tong-hop.aspx?ItemID=8932",
            DownloadUrl: "https://moet.gov.vn/content/tintuc/Documents/De-tham-khao-2025/Hoa-hoc-2025.pdf",
            PublishedDate: "2024-10-18",
            Description: "Đề tham khảo chính thức chuẩn cấu trúc 2025 môn Hóa học.",
            FileSizeBytes: 1510000,
            IsAlreadyIngested: false,
            CuratedReason: "Đề tham khảo chính thức Bộ GD&ĐT."
        ),

        // ==================== LỊCH SỬ ====================
        new ExamCandidate(
            Id: "curated-his-moet-2025",
            Title: "Đề tham khảo Tốt nghiệp THPT 2025 - Bộ GD&ĐT (Môn Lịch sử)",
            SubjectCode: SubjectCode.HIS,
            SourceName: "Bộ Giáo Dục & Đào Tạo 2025",
            ExamYear: 2025,
            DocumentType: "PDF",
            SourcePageUrl: "https://moet.gov.vn/tintuc/Pages/tin-tong-hop.aspx?ItemID=8932",
            DownloadUrl: "https://moet.gov.vn/content/tintuc/Documents/De-tham-khao-2025/Lich-su-2025.pdf",
            PublishedDate: "2024-10-18",
            Description: "Đề thi Lịch sử 28 câu (24 câu P.I, 4 câu P.II Đúng/Sai).",
            FileSizeBytes: 1280000,
            IsAlreadyIngested: false,
            CuratedReason: "Đề tham khảo chính thức Bộ GD&ĐT."
        ),

        // ==================== KINH TẾ & PHÁP LUẬT ====================
        new ExamCandidate(
            Id: "curated-gdktpl-moet-2025",
            Title: "Đề tham khảo Tốt nghiệp THPT 2025 - Bộ GD&ĐT (Môn KT & PL)",
            SubjectCode: SubjectCode.GDKTPL,
            SourceName: "Bộ Giáo Dục & Đào Tạo 2025",
            ExamYear: 2025,
            DocumentType: "PDF",
            SourcePageUrl: "https://moet.gov.vn/tintuc/Pages/tin-tong-hop.aspx?ItemID=8932",
            DownloadUrl: "https://moet.gov.vn/content/tintuc/Documents/De-tham-khao-2025/GDKTPL-2025.pdf",
            PublishedDate: "2024-10-18",
            Description: "Cấu trúc đề mới theo chương trình GDPT 2018.",
            FileSizeBytes: 1320000,
            IsAlreadyIngested: false,
            CuratedReason: "Đề tham khảo chính thức Bộ GD&ĐT."
        ),

        // ==================== TIẾNG ANH ====================
        new ExamCandidate(
            Id: "curated-eng-moet-2025",
            Title: "Đề tham khảo Tốt nghiệp THPT 2025 - Bộ GD&ĐT (Môn Tiếng Anh)",
            SubjectCode: SubjectCode.ENG,
            SourceName: "Bộ Giáo Dục & Đào Tạo 2025",
            ExamYear: 2025,
            DocumentType: "PDF",
            SourcePageUrl: "https://moet.gov.vn/tintuc/Pages/tin-tong-hop.aspx?ItemID=8932",
            DownloadUrl: "https://moet.gov.vn/content/tintuc/Documents/De-tham-khao-2025/Tieng-Anh-2025.pdf",
            PublishedDate: "2024-10-18",
            Description: "Đề thi gồm 40 câu trắc nghiệm đọc hiểu, ngữ pháp và sắp xếp đoạn văn.",
            FileSizeBytes: 1400000,
            IsAlreadyIngested: false,
            CuratedReason: "Đề tham khảo chính thức Bộ GD&ĐT."
        )
    ];

    public Task<IReadOnlyList<ExamCandidate>> DiscoverAsync(
        SubjectCode? subjectCode = null,
        short? year = null,
        CancellationToken ct = default)
    {
        IEnumerable<ExamCandidate> query = CuratedCandidates;

        if (subjectCode.HasValue)
        {
            query = query.Where(c => c.SubjectCode == subjectCode.Value);
        }

        if (year.HasValue)
        {
            query = query.Where(c => c.ExamYear == year.Value);
        }

        return Task.FromResult<IReadOnlyList<ExamCandidate>>(query.ToList());
    }
}
