using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using OmniPrep.Domain.Entities;
using OmniPrep.Domain.Enums;
using OmniPrep.Domain.Interfaces;
using OmniPrep.Infrastructure.Data;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace OmniPrep.Infrastructure.Repositories;

// ============================================================
// OmniPrep.Infrastructure - Repository Implementations
// ============================================================

/// <summary>
/// Repository cho Question entity.
/// Triển khai tìm kiếm semantic (pgvector) và rút câu hỏi cho đề thi.
/// </summary>
public sealed class QuestionRepository : IQuestionRepository
{
    private readonly OmniPrepDbContext _db;

    public QuestionRepository(OmniPrepDbContext db)
    {
        _db = db;
    }

    public async Task<Question?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.Questions
            .Include(q => q.Subject)
            .FirstOrDefaultAsync(q => q.Id == id, ct);

    public async Task<IReadOnlyList<Question>> GetByIdsAsync(
        IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var idList = ids.ToList();
        // Tải tất cả cùng một query để tránh N+1
        return await _db.Questions
            .Include(q => q.Subject)
            .Where(q => idList.Contains(q.Id))
            .ToListAsync(ct);
    }

    public async Task<Question?> GetByHashAsync(string contentHash, CancellationToken ct = default)
        => await _db.Questions
            .FirstOrDefaultAsync(q => q.ContentHash == contentHash, ct);

    /// <summary>
    /// Tìm câu hỏi tương đồng ngữ nghĩa sử dụng HNSW index của pgvector.
    /// Sử dụng Cosine Distance - ngưỡng 0.08 tương đương ~92% tương đồng.
    /// </summary>
    public async Task<IReadOnlyList<(Guid QuestionId, double CosineDistance, double SimilarityPercent)>>
        FindSimilarAsync(
            float[] embedding,
            Guid subjectId,
            double threshold = 0.08,
            int limit = 5,
            CancellationToken ct = default)
    {
        var vector = new Pgvector.Vector(embedding);

        // Sử dụng toán tử <=> (cosine distance) của pgvector
        // HNSW index sẽ tự động được sử dụng nhờ cấu hình trong migration
        var results = await _db.Questions
            .Where(q => q.SubjectId == subjectId
                     && q.IsActive
                     && q.ContentEmbedding != null)
            .Select(q => new
            {
                q.Id,
                Distance = q.ContentEmbedding!.CosineDistance(vector)
            })
            .Where(x => x.Distance < threshold)
            .OrderBy(x => x.Distance)
            .Take(limit)
            .ToListAsync(ct);

        return results
            .Select(r => (r.Id, (double)r.Distance, (double)((1 - r.Distance) * 100)))
            .ToList();
    }

    /// <summary>
    /// Rút câu hỏi ngẫu nhiên theo format, LOẠI 100% câu đã làm đúng.
    /// Tránh N+1: Load danh sách câu đã làm đúng trước, sau đó filter.
    /// </summary>
    public async Task<IReadOnlyList<Question>> DrawForExamAsync(
        Guid subjectId,
        Guid userId,
        QuestionFormat format,
        int count,
        CancellationToken ct = default)
    {
        // Load ID câu đã làm đúng của user (một query riêng biệt)
        var correctQuestionIds = await _db.UserQuestionHistories
            .Where(h => h.UserId == userId && h.Status == AnswerStatus.Correct)
            .Select(h => h.QuestionId)
            .Distinct()
            .ToListAsync(ct);

        // Rút câu ngẫu nhiên từ các câu chưa làm đúng
        return await _db.Questions
            .Where(q => q.SubjectId == subjectId
                     && q.Format == format
                     && q.IsActive
                     && q.IsVerified
                     && !correctQuestionIds.Contains(q.Id))
            .OrderBy(_ => EF.Functions.Random())
            .Take(count)
            .ToListAsync(ct);
    }

    public async Task AddAsync(Question question, CancellationToken ct = default)
        => await _db.Questions.AddAsync(question, ct);

    public async Task UpdateAsync(Question question, CancellationToken ct = default)
    {
        _db.Entry(question).State = EntityState.Modified;
        await Task.CompletedTask;
    }

    public async Task MergeSourceTagsAsync(
        Guid existingId, string newSourceTag, CancellationToken ct = default)
    {
        // Cập nhật trực tiếp bằng ExecuteUpdateAsync để tránh load toàn bộ entity
        await _db.Questions
            .Where(q => q.Id == existingId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(q => q.OccurrenceCount, q => q.OccurrenceCount + 1)
                .SetProperty(q => q.UpdatedAt, DateTimeOffset.UtcNow),
                ct);

        // Append source tag (cần load entity vì array append phức tạp hơn)
        var question = await _db.Questions.FindAsync([existingId], ct);
        if (question is not null && !question.SourceTags.Contains(newSourceTag))
        {
            question.SourceTags.Add(newSourceTag);
            _db.Questions.Update(question);
        }
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
        => await _db.SaveChangesAsync(ct);
}

/// <summary>Repository cho Subject entity.</summary>
public sealed class SubjectRepository : ISubjectRepository
{
    private readonly OmniPrepDbContext _db;

    public SubjectRepository(OmniPrepDbContext db) => _db = db;

    public async Task<Subject?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.Subjects.FindAsync([id], ct);

    public async Task<Subject?> GetByCodeAsync(SubjectCode code, CancellationToken ct = default)
        => await _db.Subjects.FirstOrDefaultAsync(s => s.Code == code && s.IsActive, ct);

    public async Task<IReadOnlyList<Subject>> GetAllActiveAsync(CancellationToken ct = default)
        => await _db.Subjects.Where(s => s.IsActive).ToListAsync(ct);
}

/// <summary>Repository cho GeneratedExam entity.</summary>
public sealed class ExamRepository : IExamRepository
{
    private readonly OmniPrepDbContext _db;

    public ExamRepository(OmniPrepDbContext db) => _db = db;

    public async Task<GeneratedExam?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.GeneratedExams
            .Include(e => e.Subject)
            .FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task AddAsync(GeneratedExam exam, CancellationToken ct = default)
        => await _db.GeneratedExams.AddAsync(exam, ct);

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
        => await _db.SaveChangesAsync(ct);
}

/// <summary>Repository cho ExamAttempt và UserQuestionHistory.</summary>
public sealed class AttemptRepository : IAttemptRepository
{
    private readonly OmniPrepDbContext _db;

    public AttemptRepository(OmniPrepDbContext db) => _db = db;

    public async Task<ExamAttempt?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.ExamAttempts
            .Include(a => a.Exam).ThenInclude(e => e.Subject)
            .FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task<IReadOnlyList<ExamAttempt>> GetUserAttemptsAsync(
        Guid userId, int limit = 20, CancellationToken ct = default)
        => await _db.ExamAttempts
            .Include(a => a.Exam).ThenInclude(e => e.Subject)
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.StartedAt)
            .Take(limit)
            .ToListAsync(ct);

    public async Task AddAsync(ExamAttempt attempt, CancellationToken ct = default)
        => await _db.ExamAttempts.AddAsync(attempt, ct);

    public Task UpdateAsync(ExamAttempt attempt, CancellationToken ct = default)
    {
        _db.ExamAttempts.Update(attempt);
        return Task.CompletedTask;
    }

    public async Task AddQuestionHistoryAsync(UserQuestionHistory history, CancellationToken ct = default)
        => await _db.UserQuestionHistories.AddAsync(history, ct);

    public async Task AddQuestionHistoriesAsync(
        IEnumerable<UserQuestionHistory> histories, CancellationToken ct = default)
        => await _db.UserQuestionHistories.AddRangeAsync(histories, ct);

    public async Task<IReadOnlyList<AttemptAnswer>> GetAttemptAnswersAsync(Guid attemptId, CancellationToken ct = default)
        => await _db.AttemptAnswers.Where(a => a.AttemptId == attemptId).ToListAsync(ct);

    public async Task<IReadOnlyList<UserQuestionHistory>> GetAttemptQuestionHistoryAsync(Guid attemptId, CancellationToken ct = default)
        => await _db.UserQuestionHistories.Where(h => h.AttemptId == attemptId).ToListAsync(ct);

    public async Task SaveAttemptAnswersAsync(Guid userId, Guid attemptId, IReadOnlyList<AttemptAnswer> answers, CancellationToken ct = default)
    {
        foreach (var answer in answers)
        {
            var serializedAnswer = answer.Answer is null
                ? null
                : JsonSerializer.Serialize(answer.Answer, JsonSerializerOptions.Default);

            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO attempt_answers (attempt_id, user_id, question_id, answer, time_spent_seconds, updated_at)
                VALUES ({attemptId}, {userId}, {answer.QuestionId}, CAST({serializedAnswer} AS jsonb), {answer.TimeSpentSeconds}, {answer.UpdatedAt})
                ON CONFLICT (attempt_id, question_id) DO UPDATE SET
                    user_id = EXCLUDED.user_id,
                    answer = EXCLUDED.answer,
                    time_spent_seconds = EXCLUDED.time_spent_seconds,
                    updated_at = EXCLUDED.updated_at
                """, ct);
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAttemptAnswersAsync(Guid attemptId, CancellationToken ct = default)
    {
        var rows = await _db.AttemptAnswers.Where(a => a.AttemptId == attemptId).ToListAsync(ct);
        _db.AttemptAnswers.RemoveRange(rows);
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
        => await _db.SaveChangesAsync(ct);
}

/// <summary>Repository cho UserTopicMastery - Heatmap data.</summary>
public sealed class MasteryRepository : IMasteryRepository
{
    private readonly OmniPrepDbContext _db;

    public MasteryRepository(OmniPrepDbContext db) => _db = db;

    public async Task<IReadOnlyList<UserTopicMastery>> GetUserMasteryAsync(
        Guid userId, Guid subjectId, CancellationToken ct = default)
        => await _db.UserTopicMasteries
            .Where(m => m.UserId == userId && m.SubjectId == subjectId)
            .OrderByDescending(m => m.MasteryRate)
            .ToListAsync(ct);

    /// <summary>
    /// Upsert mastery sau mỗi câu trả lời.
    /// Gọi SQL Function update_user_topic_mastery() trong Supabase.
    /// </summary>
    public async Task UpsertAsync(
        Guid userId, Guid subjectId, string topic, bool isCorrect,
        CancellationToken ct = default)
    {
        // Dùng ExecuteUpdateAsync thay vì gọi stored procedure để tương thích EF
        var existing = await _db.UserTopicMasteries
            .FirstOrDefaultAsync(m => m.UserId == userId
                                   && m.SubjectId == subjectId
                                   && m.Topic == topic, ct);

        if (existing is null)
        {
            await _db.UserTopicMasteries.AddAsync(new UserTopicMastery
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                SubjectId = subjectId,
                Topic = topic,
                TotalAttempts = 1,
                TotalCorrect = isCorrect ? 1 : 0,
                MasteryRate = isCorrect ? 1.0m : 0.0m,
                LastPracticed = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }, ct);
        }
        else
        {
            existing.TotalAttempts++;
            if (isCorrect) existing.TotalCorrect++;
            existing.MasteryRate = (decimal)existing.TotalCorrect / existing.TotalAttempts;
            existing.LastPracticed = DateTimeOffset.UtcNow;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            _db.UserTopicMasteries.Update(existing);
        }

        await _db.SaveChangesAsync(ct);
    }
}

/// <summary>Repository cho ExamPaper.</summary>
public sealed class ExamPaperRepository : IExamPaperRepository
{
    private readonly OmniPrepDbContext _db;

    public ExamPaperRepository(OmniPrepDbContext db) => _db = db;

    public async Task<ExamPaper?> GetByFileHashAsync(string fileHash, CancellationToken ct = default)
        => await _db.ExamPapers.FirstOrDefaultAsync(p => p.FileHash == fileHash, ct);

    public async Task<IReadOnlyList<ExamPaper>> ClaimPendingPapersAsync(int batchSize = 5, CancellationToken ct = default)
    {
        if (batchSize <= 0) return [];

        await _db.Database.ExecuteSqlRawAsync("""
            UPDATE exam_papers
            SET status = 'FAILED',
                processing_started_at = NULL,
                processing_lease_id = NULL,
                processed_at = NOW(),
                error_message = COALESCE(error_message, 'Processing lease expired after maximum attempts.')
            WHERE status = 'PROCESSING'
              AND processing_attempts >= 3
              AND processing_started_at < NOW() - INTERVAL '5 minutes'
            """, ct);

        var claimedIds = await _db.Database.SqlQuery<Guid>($"""
            WITH next_papers AS (
                SELECT id
                FROM exam_papers
                WHERE raw_content IS NOT NULL
                  AND processing_attempts < 3
                  AND (
                    status = 'PENDING'
                    OR (status = 'PROCESSING' AND processing_started_at < NOW() - INTERVAL '5 minutes')
                  )
                ORDER BY submitted_at, id
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED
            )
            UPDATE exam_papers AS paper
            SET status = 'PROCESSING',
                processing_attempts = paper.processing_attempts + 1,
                processing_started_at = NOW(),
                processing_lease_id = gen_random_uuid(),
                processed_at = NULL,
                error_message = NULL
            FROM next_papers
            WHERE paper.id = next_papers.id
            RETURNING paper.id AS "Value"
            """).ToListAsync(ct);

        if (claimedIds.Count == 0) return [];

        return await _db.ExamPapers
            .Include(p => p.Subject)
            .Include(p => p.Source)
            .Where(p => claimedIds.Contains(p.Id))
            .OrderBy(p => p.SubmittedAt)
            .ToListAsync(ct);
    }

    public async Task<bool> RefreshProcessingLeaseAsync(Guid paperId, Guid leaseId, CancellationToken ct = default)
        => await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE exam_papers
            SET processing_started_at = NOW()
            WHERE id = {paperId} AND processing_lease_id = {leaseId} AND status = 'PROCESSING'
            """, ct) == 1;

    public async Task<bool> UpdateClaimedPaperAsync(ExamPaper paper, Guid leaseId, CancellationToken ct = default)
        => await _db.ExamPapers
            .Where(p => p.Id == paper.Id && p.Status == IngestionStatus.Processing && p.ProcessingLeaseId == leaseId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, paper.Status)
                .SetProperty(p => p.TotalExtracted, paper.TotalExtracted)
                .SetProperty(p => p.TotalNew, paper.TotalNew)
                .SetProperty(p => p.TotalMerged, paper.TotalMerged)
                .SetProperty(p => p.ErrorMessage, paper.ErrorMessage)
                .SetProperty(p => p.ProcessedAt, paper.ProcessedAt)
                .SetProperty(p => p.ProcessingStartedAt, paper.ProcessingStartedAt)
                .SetProperty(p => p.ProcessingLeaseId, (Guid?)null), ct) == 1;

    public async Task AddAsync(ExamPaper paper, CancellationToken ct = default)
        => await _db.ExamPapers.AddAsync(paper, ct);

    public Task UpdateAsync(ExamPaper paper, CancellationToken ct = default)
    {
        _db.ExamPapers.Update(paper);
        return Task.CompletedTask;
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
        => await _db.SaveChangesAsync(ct);
}
