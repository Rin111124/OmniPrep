namespace OmniPrep.Domain.Enums;

// ============================================================
// OmniPrep.Domain - Enums & Value Objects
// ============================================================

/// <summary>Mã môn học theo chuẩn Bộ GD&ĐT</summary>
public enum SubjectCode
{
    GEO,      // Địa lý
    GDKTPL,   // Giáo dục Kinh tế & Pháp luật
    MATH,     // Toán
    PHYS,     // Vật lý
    CHEM,     // Hóa học
    BIO,      // Sinh học
    LIT,      // Ngữ văn
    ENG,      // Tiếng Anh
    HIS,      // Lịch sử
    CIVICS    // Giáo dục công dân
}

/// <summary>Vai trò người dùng trong hệ thống phân quyền (RBAC)</summary>
public enum UserRole
{
    /// <summary>Học sinh: Luyện thi, xem điểm, AI Tutor, sổ tay câu sai</summary>
    Student,
    /// <summary>Giáo viên: Duyệt đề, quản lý câu hỏi, xem phổ điểm học sinh</summary>
    Teacher,
    /// <summary>Quản trị viên: Toàn quyền hệ thống, điều khiển Pipeline bóc tách 24/7</summary>
    Admin
}


/// <summary>Định dạng câu hỏi theo chuẩn Bộ GD&ĐT 2025</summary>
public enum QuestionFormat
{
    /// <summary>Phần I: Trắc nghiệm 4 lựa chọn (0.25đ/câu)</summary>
    MultipleChoice,
    /// <summary>Phần II: Đúng/Sai 4 ý - chấm lũy tiến</summary>
    TrueFalse,
    /// <summary>Phần III: Trả lời ngắn có dung sai</summary>
    ShortAnswer
}

/// <summary>Mức độ nhận thức theo thang Bloom</summary>
public enum DifficultyLevel
{
    NhanBiet,    // Nhận biết
    ThongHieu,   // Thông hiểu
    VanDung,     // Vận dụng
    VanDungCao   // Vận dụng cao
}

/// <summary>Trạng thái xử lý đề thi trong Worker Service</summary>
public enum IngestionStatus
{
    Pending,     // Đang chờ xử lý
    Processing,  // Đang bóc tách
    Completed,   // Hoàn thành
    Failed,      // Thất bại
    Duplicate    // Đề bị trùng hoàn toàn
}

/// <summary>Kết quả trả lời của học sinh</summary>
public enum AnswerStatus
{
    Correct,  // Đúng
    Wrong,    // Sai
    Skipped   // Bỏ qua
}

/// <summary>Mức độ thành thạo chuyên đề - dùng cho màu Heatmap</summary>
public enum MasteryLevel
{
    NotStarted,  // Chưa học (Xám)
    Beginner,    // Mới bắt đầu 0-20% (Đỏ)
    Developing,  // Đang phát triển 20-40% (Cam)
    Proficient,  // Khá 40-60% (Vàng)
    Advanced,    // Giỏi 60-80% (Xanh lá nhạt)
    Expert       // Xuất sắc 80-100% (Xanh lá đậm)
}
