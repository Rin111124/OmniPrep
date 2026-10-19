using OmniPrep.Domain.Enums;

namespace OmniPrep.Domain.Entities;

/// <summary>
/// Sổ tay bẫy đề thi - Lưu trữ các câu hỏi học sinh làm sai trên server
/// </summary>
public class UserMistake
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid QuestionId { get; set; }
    public SubjectCode SubjectCode { get; set; }
    public Dictionary<string, object>? StudentAnswer { get; set; }
    public bool Mastered { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Navigation
    public Profile User { get; set; } = null!;
    public Question Question { get; set; } = null!;
}
