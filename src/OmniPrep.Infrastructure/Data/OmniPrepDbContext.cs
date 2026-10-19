using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.NameTranslation;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OmniPrep.Domain.Entities;
using OmniPrep.Domain.Enums;
using OmniPrep.Domain.ValueObjects;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using System.Text.Json;

namespace OmniPrep.Infrastructure.Data;

// ============================================================
// OmniPrep.Infrastructure - EF Core DbContext
// Ánh xạ Entity <-> Database Table
// ============================================================

public class OmniPrepDbContext : DbContext
{
    public OmniPrepDbContext(DbContextOptions<OmniPrepDbContext> options) : base(options) { }

    // DbSets
    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<ExamSource> ExamSources => Set<ExamSource>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<ExamPaper> ExamPapers => Set<ExamPaper>();
    public DbSet<GeneratedExam> GeneratedExams => Set<GeneratedExam>();
    public DbSet<ExamAttempt> ExamAttempts => Set<ExamAttempt>();
    public DbSet<UserQuestionHistory> UserQuestionHistories => Set<UserQuestionHistory>();
    public DbSet<AttemptAnswer> AttemptAnswers => Set<AttemptAnswer>();
    public DbSet<UserTopicMastery> UserTopicMasteries => Set<UserTopicMastery>();
    public DbSet<AiTutorSession> AiTutorSessions => Set<AiTutorSession>();
    public DbSet<UserMistake> UserMistakes => Set<UserMistake>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Kích hoạt pgvector extension
        modelBuilder.HasPostgresExtension("vector");

        // Áp dụng tất cả configuration từ Assembly này
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OmniPrepDbContext).Assembly);

        // Database enum labels are uppercase snake case; preserve the schema's exact labels.
        var enumNameTranslator = new UppercaseSnakeCaseNameTranslator();
        modelBuilder.HasPostgresEnum<SubjectCode>(name: "subject_code", nameTranslator: enumNameTranslator);
        modelBuilder.HasPostgresEnum<QuestionFormat>(name: "question_format", nameTranslator: enumNameTranslator);
        modelBuilder.HasPostgresEnum<DifficultyLevel>(name: "difficulty_level", nameTranslator: enumNameTranslator);
        modelBuilder.HasPostgresEnum<IngestionStatus>(name: "ingestion_status", nameTranslator: enumNameTranslator);
        modelBuilder.HasPostgresEnum<AnswerStatus>(name: "answer_status", nameTranslator: enumNameTranslator);
        modelBuilder.HasPostgresEnum<UserRole>(name: "user_role", nameTranslator: enumNameTranslator);
    }
}

internal sealed class UppercaseSnakeCaseNameTranslator : INpgsqlNameTranslator
{
    private readonly NpgsqlSnakeCaseNameTranslator _snakeCase = new();

    public string TranslateTypeName(string clrName) => _snakeCase.TranslateTypeName(clrName);

    public string TranslateMemberName(string clrName) =>
        _snakeCase.TranslateMemberName(clrName).ToUpperInvariant();
}

// ============================================================
// ENTITY CONFIGURATIONS (Fluent API)
// ============================================================

public class ProfileConfiguration : IEntityTypeConfiguration<Profile>
{
    public void Configure(EntityTypeBuilder<Profile> builder)
    {
        builder.ToTable("profiles");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.FullName).HasColumnName("full_name").HasMaxLength(255);
        builder.Property(p => p.Grade).HasColumnName("grade");
        builder.Property(p => p.SchoolName).HasColumnName("school_name").HasMaxLength(500);
        builder.Property(p => p.AvatarUrl).HasColumnName("avatar_url");
        builder.Property(p => p.Role).HasColumnName("role");
        builder.Property(p => p.CreatedAt).HasColumnName("created_at");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");

        // JSONB: score_goals lưu dạng Dictionary
        builder.Property(p => p.ScoreGoals)
            .HasColumnName("score_goals")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                v => JsonSerializer.Deserialize<Dictionary<string, decimal>>(v, JsonSerializerOptions.Default) ?? new());
    }
}

public class SubjectConfiguration : IEntityTypeConfiguration<Subject>
{
    public void Configure(EntityTypeBuilder<Subject> builder)
    {
        builder.ToTable("subjects");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");
        builder.Property(s => s.Code).HasColumnName("code");
        builder.Property(s => s.Name).HasColumnName("name").HasMaxLength(100);
        builder.Property(s => s.TotalScore).HasColumnName("total_score").HasPrecision(4, 2);
        builder.Property(s => s.IsActive).HasColumnName("is_active");
        builder.Property(s => s.CreatedAt).HasColumnName("created_at");
        builder.HasIndex(s => s.Name).IsUnique();

        // JSONB: exam_matrix
        builder.Property(s => s.ExamMatrix)
            .HasColumnName("exam_matrix")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                v => JsonSerializer.Deserialize<ExamMatrix>(v, JsonSerializerOptions.Default) ?? new());

        // Array: topics
        builder.Property(s => s.Topics)
            .HasColumnName("topics")
            .HasColumnType("text[]")
            .HasConversion(
                v => v.ToArray(),
                v => v.ToList());

        builder.HasIndex(s => s.Code).IsUnique();
    }
}

public class QuestionConfiguration : IEntityTypeConfiguration<Question>
{
    public void Configure(EntityTypeBuilder<Question> builder)
    {
        builder.ToTable("questions");
        builder.HasKey(q => q.Id);
        builder.Property(q => q.Id).HasColumnName("id");
        builder.Property(q => q.SubjectId).HasColumnName("subject_id");
        builder.Property(q => q.Content).HasColumnName("content");
        builder.Property(q => q.AttachedTable).HasColumnName("attached_table");
        builder.Property(q => q.Format).HasColumnName("format");
        builder.Property(q => q.Difficulty).HasColumnName("difficulty");
        builder.Property(q => q.Topic).HasColumnName("topic").HasMaxLength(255);
        builder.Property(q => q.Explanation).HasColumnName("explanation");
        builder.Property(q => q.ContentHash).HasColumnName("content_hash").HasMaxLength(64).IsFixedLength();
        builder.Property(q => q.FirstSeenYear).HasColumnName("first_seen_year");
        builder.Property(q => q.OccurrenceCount).HasColumnName("occurrence_count");
        builder.Property(q => q.IsVerified).HasColumnName("is_verified");
        builder.Property(q => q.IsActive).HasColumnName("is_active");
        builder.Property(q => q.ReviewedBy).HasColumnName("reviewed_by");
        builder.Property(q => q.ReviewedAt).HasColumnName("reviewed_at");
        builder.Property(q => q.ReviewNote).HasColumnName("review_note");
        builder.Property(q => q.CreatedAt).HasColumnName("created_at");
        builder.Property(q => q.UpdatedAt).HasColumnName("updated_at");

        // VECTOR: content_embedding (768 chiều)
        builder.Property(q => q.ContentEmbedding)
            .HasColumnName("content_embedding")
            .HasColumnType("vector(768)");

        // JSONB: answer_data
        builder.Property(q => q.AnswerData)
            .HasColumnName("answer_data")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                v => JsonSerializer.Deserialize<AnswerData>(v, JsonSerializerOptions.Default) ?? new());

        // JSONB: distractor_analysis
        builder.Property(q => q.DistractorAnalysis)
            .HasColumnName("distractor_analysis")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, JsonSerializerOptions.Default) ?? new());

        // Array: image_urls, source_tags
        builder.Property(q => q.ImageUrls)
            .HasColumnName("image_urls")
            .HasColumnType("text[]")
            .HasConversion(v => v.ToArray(), v => v.ToList());

        builder.Property(q => q.SourceTags)
            .HasColumnName("source_tags")
            .HasColumnType("text[]")
            .HasConversion(v => v.ToArray(), v => v.ToList());

        // Index: SHA-256 unique
        builder.HasIndex(q => q.ContentHash).IsUnique().HasDatabaseName("idx_questions_content_hash");

        // Relationship
        builder.HasOne(q => q.Subject)
            .WithMany(s => s.Questions)
            .HasForeignKey(q => q.SubjectId);
    }
}

public class ExamSourceConfiguration : IEntityTypeConfiguration<ExamSource>
{
    public void Configure(EntityTypeBuilder<ExamSource> builder)
    {
        builder.ToTable("exam_sources");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");
        builder.Property(s => s.Name).HasColumnName("name").HasMaxLength(255);
        builder.Property(s => s.Province).HasColumnName("province");
        builder.Property(s => s.SourceType).HasColumnName("source_type");
        builder.Property(s => s.WebsiteUrl).HasColumnName("website_url");
        builder.Property(s => s.IsActive).HasColumnName("is_active");
        builder.Property(s => s.CreatedAt).HasColumnName("created_at");
    }
}

public class ExamPaperConfiguration : IEntityTypeConfiguration<ExamPaper>
{
    public void Configure(EntityTypeBuilder<ExamPaper> builder)
    {
        builder.ToTable("exam_papers");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.SourceId).HasColumnName("source_id");
        builder.Property(p => p.SubjectId).HasColumnName("subject_id");
        builder.Property(p => p.Title).HasColumnName("title");
        builder.Property(p => p.ExamYear).HasColumnName("exam_year");
        builder.Property(p => p.ExamType).HasColumnName("exam_type");
        builder.Property(p => p.FileUrl).HasColumnName("file_url");
        builder.Property(p => p.FileHash).HasColumnName("file_hash");
        builder.Property(p => p.RawContent).HasColumnName("raw_content");
        builder.Property(p => p.Status).HasColumnName("status");
        builder.Property(p => p.TotalExtracted).HasColumnName("total_extracted");
        builder.Property(p => p.TotalNew).HasColumnName("total_new");
        builder.Property(p => p.TotalMerged).HasColumnName("total_merged");
        builder.Property(p => p.ErrorMessage).HasColumnName("error_message");
        builder.Property(p => p.ProcessingAttempts).HasColumnName("processing_attempts");
        builder.Property(p => p.ProcessingStartedAt).HasColumnName("processing_started_at");
        builder.Property(p => p.ProcessingLeaseId).HasColumnName("processing_lease_id");
        builder.Property(p => p.SubmittedAt).HasColumnName("submitted_at");
        builder.Property(p => p.ProcessedAt).HasColumnName("processed_at");
        builder.HasOne(p => p.Subject).WithMany().HasForeignKey(p => p.SubjectId);
        builder.HasOne(p => p.Source).WithMany(s => s.ExamPapers).HasForeignKey(p => p.SourceId);
    }
}

public class GeneratedExamConfiguration : IEntityTypeConfiguration<GeneratedExam>
{
    public void Configure(EntityTypeBuilder<GeneratedExam> builder)
    {
        builder.ToTable("generated_exams");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.UserId).HasColumnName("user_id");
        builder.Property(e => e.SubjectId).HasColumnName("subject_id");
        builder.Property(e => e.DurationMinutes).HasColumnName("duration_minutes");
        builder.Property(e => e.ExpiresAt).HasColumnName("expires_at");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");

        // Array: question_ids
        builder.Property(e => e.QuestionIds)
            .HasColumnName("question_ids")
            .HasColumnType("uuid[]")
            .HasConversion(v => v.ToArray(), v => v.ToList());

        // JSONB: matrix_snapshot
        builder.Property(e => e.MatrixSnapshot)
            .HasColumnName("matrix_snapshot")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                v => JsonSerializer.Deserialize<ExamMatrix>(v, JsonSerializerOptions.Default) ?? new());
    }
}

public class ExamAttemptConfiguration : IEntityTypeConfiguration<ExamAttempt>
{
    public void Configure(EntityTypeBuilder<ExamAttempt> builder)
    {
        builder.ToTable("exam_attempts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");
        builder.Property(a => a.UserId).HasColumnName("user_id");
        builder.Property(a => a.ExamId).HasColumnName("exam_id");
        builder.Property(a => a.SubjectId).HasColumnName("subject_id");
        builder.Property(a => a.ScorePart1).HasColumnName("score_part1").HasPrecision(4, 2);
        builder.Property(a => a.ScorePart2).HasColumnName("score_part2").HasPrecision(4, 2);
        builder.Property(a => a.ScorePart3).HasColumnName("score_part3").HasPrecision(4, 2);
        builder.Property(a => a.TotalScore).HasColumnName("total_score").HasPrecision(4, 2);
        builder.Property(a => a.TimeSpentSeconds).HasColumnName("time_spent_seconds");
        builder.Property(a => a.IsSubmitted).HasColumnName("is_submitted");
        builder.Property(a => a.SubmittedAt).HasColumnName("submitted_at");
        builder.Property(a => a.StartedAt).HasColumnName("started_at");

        builder.HasOne(a => a.Exam)
            .WithMany(e => e.Attempts)
            .HasForeignKey(a => a.ExamId);

        builder.HasIndex(a => new { a.UserId, a.SubjectId, a.StartedAt })
            .HasDatabaseName("idx_attempts_user_subject");
    }
}

public class UserQuestionHistoryConfiguration : IEntityTypeConfiguration<UserQuestionHistory>
{
    public void Configure(EntityTypeBuilder<UserQuestionHistory> builder)
    {
        builder.ToTable("user_question_history");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).HasColumnName("id");
        builder.Property(h => h.UserId).HasColumnName("user_id");
        builder.Property(h => h.AttemptId).HasColumnName("attempt_id");
        builder.Property(h => h.QuestionId).HasColumnName("question_id");
        builder.Property(h => h.Status).HasColumnName("status");
        builder.Property(h => h.PointsEarned).HasColumnName("points_earned").HasPrecision(4, 2);
        builder.Property(h => h.TimeSpentSeconds).HasColumnName("time_spent_seconds");
        builder.Property(h => h.AiExplanationViewed).HasColumnName("ai_explanation_viewed");
        builder.Property(h => h.AnsweredAt).HasColumnName("answered_at");

        // JSONB: user_answer
        builder.Property(h => h.UserAnswer)
            .HasColumnName("user_answer")
            .HasColumnType("jsonb")
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                v => v == null ? null : JsonSerializer.Deserialize<Dictionary<string, object>>(v, JsonSerializerOptions.Default));

        builder.HasIndex(h => new { h.UserId, h.QuestionId, h.Status })
            .HasDatabaseName("idx_uqh_user_question");
    }
}

public class AttemptAnswerConfiguration : IEntityTypeConfiguration<AttemptAnswer>
{
    public void Configure(EntityTypeBuilder<AttemptAnswer> builder)
    {
        builder.ToTable("attempt_answers");
        builder.HasKey(a => new { a.AttemptId, a.QuestionId });
        builder.Property(a => a.AttemptId).HasColumnName("attempt_id");
        builder.Property(a => a.UserId).HasColumnName("user_id");
        builder.Property(a => a.QuestionId).HasColumnName("question_id");
        builder.Property(a => a.Answer).HasColumnName("answer").HasColumnType("jsonb")
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                v => v == null ? null : JsonSerializer.Deserialize<Dictionary<string, object>>(v, JsonSerializerOptions.Default));
        builder.Property(a => a.TimeSpentSeconds).HasColumnName("time_spent_seconds");
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at");
    }
}

public class UserTopicMasteryConfiguration : IEntityTypeConfiguration<UserTopicMastery>
{
    public void Configure(EntityTypeBuilder<UserTopicMastery> builder)
    {
        builder.ToTable("user_topic_mastery");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id");
        builder.Property(m => m.UserId).HasColumnName("user_id");
        builder.Property(m => m.SubjectId).HasColumnName("subject_id");
        builder.Property(m => m.Topic).HasColumnName("topic").HasMaxLength(255);
        builder.Property(m => m.TotalAttempts).HasColumnName("total_attempts");
        builder.Property(m => m.TotalCorrect).HasColumnName("total_correct");
        builder.Property(m => m.MasteryRate).HasColumnName("mastery_rate").HasPrecision(5, 4);
        builder.Property(m => m.MasteryScore).HasColumnName("mastery_score").HasPrecision(6, 2);
        builder.Property(m => m.LastPracticed).HasColumnName("last_practiced");
        builder.Property(m => m.UpdatedAt).HasColumnName("updated_at");

        // Unique constraint: một user chỉ có một bản ghi mastery cho mỗi topic trong môn
        builder.HasIndex(m => new { m.UserId, m.SubjectId, m.Topic }).IsUnique();

        builder.Ignore(m => m.Level); // Computed property, không map vào DB
    }
}

public class AiTutorSessionConfiguration : IEntityTypeConfiguration<AiTutorSession>
{
    public void Configure(EntityTypeBuilder<AiTutorSession> builder)
    {
        builder.ToTable("ai_tutor_sessions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");
        builder.Property(s => s.UserId).HasColumnName("user_id");
        builder.Property(s => s.QuestionId).HasColumnName("question_id");
        builder.Property(s => s.AttemptId).HasColumnName("attempt_id");
        builder.Property(s => s.AiResponse).HasColumnName("ai_response");
        builder.Property(s => s.TokensUsed).HasColumnName("tokens_used");
        builder.Property(s => s.CreatedAt).HasColumnName("created_at");

        // JSONB: context_sent
        builder.Property(s => s.ContextSent)
            .HasColumnName("context_sent")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                v => JsonSerializer.Deserialize<TutorContext>(v, JsonSerializerOptions.Default) ?? new());
    }
}

public class UserMistakeConfiguration : IEntityTypeConfiguration<UserMistake>
{
    public void Configure(EntityTypeBuilder<UserMistake> builder)
    {
        builder.ToTable("user_mistakes");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id");
        builder.Property(m => m.UserId).HasColumnName("user_id");
        builder.Property(m => m.QuestionId).HasColumnName("question_id");
        builder.Property(m => m.SubjectCode).HasColumnName("subject_code");
        builder.Property(m => m.Mastered).HasColumnName("mastered");
        builder.Property(m => m.Notes).HasColumnName("notes");
        builder.Property(m => m.CreatedAt).HasColumnName("created_at");
        builder.Property(m => m.UpdatedAt).HasColumnName("updated_at");

        builder.Property(m => m.StudentAnswer)
            .HasColumnName("student_answer")
            .HasColumnType("jsonb")
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                v => v == null ? null : JsonSerializer.Deserialize<Dictionary<string, object>>(v, JsonSerializerOptions.Default));

        builder.HasOne(m => m.User)
            .WithMany(p => p.Mistakes)
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.Question)
            .WithMany(q => q.UserMistakes)
            .HasForeignKey(m => m.QuestionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => new { m.UserId, m.QuestionId }).IsUnique();
        builder.HasIndex(m => new { m.UserId, m.SubjectCode });
        builder.HasIndex(m => new { m.UserId, m.Mastered });
    }
}
