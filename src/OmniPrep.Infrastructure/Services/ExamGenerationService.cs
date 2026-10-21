using OmniPrep.Domain.Entities;
using OmniPrep.Domain.Enums;
using OmniPrep.Domain.Interfaces;
using OmniPrep.Domain.ValueObjects;

namespace OmniPrep.Infrastructure.Services;

// ============================================================
// OmniPrep.Infrastructure - Exam Generation Service
// Trộn đề theo ma trận, loại câu đã làm đúng
// ============================================================

/// <summary>
/// Service tạo đề thi theo ma trận chuẩn Bộ GD&ĐT.
/// Tự động loại câu học sinh đã làm đúng để không lặp lại.
/// </summary>
public sealed class ExamGenerationService : IExamGenerationService
{
    private readonly IQuestionRepository _questionRepo;
    private readonly ISubjectRepository _subjectRepo;
    private readonly IExamRepository _examRepo;

    public ExamGenerationService(
        IQuestionRepository questionRepo,
        ISubjectRepository subjectRepo,
        IExamRepository examRepo)
    {
        _questionRepo = questionRepo;
        _subjectRepo = subjectRepo;
        _examRepo = examRepo;
    }

    public async Task<GeneratedExam> GenerateExamAsync(
        Guid userId,
        SubjectCode subjectCode,
        string? sourceName = null,
        CancellationToken ct = default)
    {
        var subject = await _subjectRepo.GetByCodeAsync(subjectCode, ct)
            ?? throw new InvalidOperationException($"Môn học không tồn tại: {subjectCode}");

        var matrix = subject.ExamMatrix;

        // Rút câu theo từng phần của ma trận (ưu tiên nguồn đề nếu được chọn)
        var part1Questions = await _questionRepo.DrawForExamAsync(
            subject.Id, userId, QuestionFormat.MultipleChoice, matrix.Part1.Count, sourceName, ct);

        var part2Questions = await _questionRepo.DrawForExamAsync(
            subject.Id, userId, QuestionFormat.TrueFalse, matrix.Part2.Count, sourceName, ct);

        var part3Questions = await _questionRepo.DrawForExamAsync(
            subject.Id, userId, QuestionFormat.ShortAnswer, matrix.Part3.Count, sourceName, ct);

        if (part1Questions.Count != matrix.Part1.Count
            || part2Questions.Count != matrix.Part2.Count
            || part3Questions.Count != matrix.Part3.Count)
        {
            throw new InvalidOperationException(
                $"Ngân hàng câu hỏi đã duyệt chưa đủ cho môn {subject.Name}. " +
                $"Cần {matrix.Part1.Count}/{matrix.Part2.Count}/{matrix.Part3.Count} câu theo ba phần, " +
                $"hiện có {part1Questions.Count}/{part2Questions.Count}/{part3Questions.Count} câu phù hợp.");
        }

        // Ghép danh sách câu theo thứ tự Phần I -> II -> III
        var allQuestions = part1Questions
            .Concat(part2Questions)
            .Concat(part3Questions)
            .ToList();

        var exam = new GeneratedExam
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            SubjectId = subject.Id,
            QuestionIds = allQuestions.Select(q => q.Id).ToList(),
            MatrixSnapshot = matrix,
            DurationMinutes = 50,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(90), // 50 phút + 40 phút buffer
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _examRepo.AddAsync(exam, ct);
        await _examRepo.SaveChangesAsync(ct);

        return exam;
    }
}
