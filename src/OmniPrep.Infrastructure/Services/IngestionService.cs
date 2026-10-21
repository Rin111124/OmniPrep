using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OmniPrep.Domain.Entities;
using OmniPrep.Domain.Enums;
using OmniPrep.Domain.Interfaces;
using OmniPrep.Domain.ValueObjects;
using OmniPrep.Infrastructure.Data;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace OmniPrep.Infrastructure.Services;

// ============================================================
// OmniPrep.Infrastructure - Ingestion Service
// Pipeline bóc tách & chống trùng lặp 2 tầng
// ============================================================

/// <summary>
/// Orchestrator cho toàn bộ pipeline nạp đề thi.
/// Thực hiện: Bóc tách -> SHA-256 dedup -> Vector dedup -> Lưu DB
/// </summary>
public sealed class IngestionService : IIngestionService
{
    private readonly IGeminiService _geminiService;
    private readonly IEmbeddingService _embeddingService;
    private readonly ISubjectStrategyFactory _strategyFactory;
    private readonly IQuestionRepository _questionRepo;
    private readonly IExamPaperRepository _paperRepo;
    private readonly ISubjectRepository _subjectRepo;
    private readonly IQuestionValidator _validator;
    private readonly ILogger<IngestionService> _logger;

    public IngestionService(
        IGeminiService geminiService,
        IEmbeddingService embeddingService,
        ISubjectStrategyFactory strategyFactory,
        IQuestionRepository questionRepo,
        IExamPaperRepository paperRepo,
        ISubjectRepository subjectRepo,
        IQuestionValidator validator,
        ILogger<IngestionService> logger)
    {
        _geminiService = geminiService;
        _embeddingService = embeddingService;
        _strategyFactory = strategyFactory;
        _questionRepo = questionRepo;
        _paperRepo = paperRepo;
        _subjectRepo = subjectRepo;
        _validator = validator;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IngestionResult> IngestExamPaperAsync(
        Guid examPaperId,
        string documentContent,
        SubjectCode subjectCode,
        string sourceName,
        short? examYear,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Bắt đầu bóc tách đề thi {PaperId} - Môn: {Subject} - Nguồn: {Source}",
            examPaperId, subjectCode, sourceName);

        // Lấy Subject từ DB
        var subject = await _subjectRepo.GetByCodeAsync(subjectCode, ct)
            ?? throw new InvalidOperationException($"Không tìm thấy môn học: {subjectCode}");

        // Lấy Strategy tương ứng môn học
        var strategy = _strategyFactory.GetStrategy(subjectCode);

        int totalExtracted = 0, totalNew = 0, totalMerged = 0, totalFailed = 0;
        var errors = new List<string>();

        // =====================================================
        // BƯỚC 1: Bóc tách câu hỏi từ document qua Gemini API (kèm Fallback)
        // =====================================================
        IReadOnlyList<ExtractedQuestion> extractedQuestions;
        try
        {
            extractedQuestions = await _geminiService.ExtractQuestionsAsync(
                documentContent, subjectCode, ct);
            totalExtracted = extractedQuestions.Count;
            _logger.LogInformation("Gemini bóc tách được {Count} câu hỏi", totalExtracted);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gemini API không khả dụng hoặc lỗi: {Msg}. Tự động kích hoạt Rule-Based Extractor dự phòng.", ex.Message);
            extractedQuestions = RuleBasedQuestionExtractor.Extract(documentContent, subjectCode, subject.Topics);
            totalExtracted = extractedQuestions.Count;
            _logger.LogInformation("Rule-Based Extractor trích xuất được {Count} câu hỏi dự phòng", totalExtracted);
        }

        if (extractedQuestions.Count == 0)
        {
            extractedQuestions = RuleBasedQuestionExtractor.Extract(documentContent, subjectCode, subject.Topics);
            totalExtracted = extractedQuestions.Count;
        }

        if (extractedQuestions.Count == 0)
        {
            var msg = "Không trích xuất được câu hỏi nào từ văn bản đã cung cấp. Vui lòng kiểm tra lại chất lượng nội dung đề.";
            errors.Add(msg);
            _logger.LogWarning(msg);
            return new IngestionResult(0, 0, 0, 0, errors, 0, 0, 0);
        }

        int part1Count = extractedQuestions.Count(q => string.Equals(q.Format, "MULTIPLE_CHOICE", StringComparison.OrdinalIgnoreCase));
        int part2Count = extractedQuestions.Count(q => string.Equals(q.Format, "TRUE_FALSE", StringComparison.OrdinalIgnoreCase));
        int part3Count = extractedQuestions.Count(q => string.Equals(q.Format, "SHORT_ANSWER", StringComparison.OrdinalIgnoreCase));

        // =====================================================
        // BƯỚC 2: Kiểm tra chất lượng (Validator) & Chống trùng lặp
        // =====================================================
        foreach (var extracted in extractedQuestions)
        {
            var validation = _validator.Validate(extracted, subjectCode);
            if (!validation.IsValid)
            {
                totalFailed++;
                var errMsg = $"Câu không đạt chuẩn: {string.Join(", ", validation.Errors)} (Nội dung: '{extracted.Content[..Math.Min(40, extracted.Content.Length)]}...')";
                errors.Add(errMsg);
                _logger.LogWarning(errMsg);
                continue;
            }

            try
            {
                var isNew = await ProcessSingleQuestionAsync(
                    extracted, subject, sourceName, examYear, strategy, ct);
                if (isNew)
                    totalNew++;
                else
                    totalMerged++;
            }
            catch (Exception ex)
            {
                totalFailed++;
                var errMsg = $"Lỗi xử lý câu '{extracted.Content[..Math.Min(50, extracted.Content.Length)]}...': {ex.Message}";
                errors.Add(errMsg);
                _logger.LogWarning(ex, errMsg);
            }
        }

        await _questionRepo.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Hoàn thành bóc tách đề {PaperId}: Trích xuất={Total}, Mới={New}, Gộp={Merged}, Lỗi={Failed}",
            examPaperId, totalExtracted, totalNew, totalMerged, totalFailed);

        return new IngestionResult(totalExtracted, totalNew, totalMerged, totalFailed, errors, part1Count, part2Count, part3Count);
    }

    /// <summary>
    /// Xử lý một câu hỏi qua đầy đủ pipeline 2 tầng chống trùng lặp.
    /// Trả về true nếu là câu mới, false nếu gộp với câu đã có.
    /// </summary>
    private async Task<bool> ProcessSingleQuestionAsync(
        ExtractedQuestion extracted,
        Subject subject,
        string sourceName,
        short? examYear,
        ISubjectStrategy strategy,
        CancellationToken ct)
    {
        // ----------------------------------------
        // TẦNG 1: SHA-256 Exact Match Deduplication
        // ----------------------------------------
        var contentHash = ComputeSha256(extracted.Content);
        var existingByHash = await _questionRepo.GetByHashAsync(contentHash, ct);

        if (existingByHash is not null)
        {
            // Trùng 100% -> Chỉ gộp tên Sở vào source_tags
            var tag = BuildSourceTag(sourceName, examYear);
            if (!existingByHash.SourceTags.Contains(tag))
            {
                await _questionRepo.MergeSourceTagsAsync(existingByHash.Id, tag, ct);
                _logger.LogDebug(
                    "SHA-256 trùng: Gộp nguồn '{Tag}' vào câu {Id}",
                    tag, existingByHash.Id);
            }
            return false;
        }

        // ----------------------------------------
        // TẦNG 2: Semantic Deduplication (pgvector)
        // ----------------------------------------
        float[]? embedding = null;
        try
        {
            embedding = await _embeddingService.GetEmbeddingAsync(extracted.Content, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không thể tạo embedding qua Gemini (chưa cấu hình API Key hoặc lỗi mạng). Bỏ qua Semantic Dedup.");
        }

        if (embedding is not null && embedding.Length > 0)
        {
            try
            {
                var similar = await _questionRepo.FindSimilarAsync(
                    embedding, subject.Id, threshold: 0.08, limit: 3, ct);

                if (similar.Count > 0)
                {
                    // Câu hỏi bị xào nấu/đổi số liệu -> Gộp nguồn vào câu gốc
                    var mostSimilar = similar[0];
                    var tag = BuildSourceTag(sourceName, examYear);
                    await _questionRepo.MergeSourceTagsAsync(mostSimilar.QuestionId, tag, ct);

                    _logger.LogInformation(
                        "Semantic trùng: {Similarity:F1}% - Gộp nguồn '{Tag}' vào câu {Id}",
                        mostSimilar.SimilarityPercent, tag, mostSimilar.QuestionId);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lỗi khi so sánh similarity pgvector: {Msg}", ex.Message);
            }
        }

        // ----------------------------------------
        // CÂU MỚI THỰC SỰ: Lưu vào Database
        // ----------------------------------------
        var question = MapToQuestion(extracted, subject.Id, contentHash, embedding, sourceName, examYear);
        await _questionRepo.AddAsync(question, ct);

        _logger.LogDebug("Câu mới: '{Content}'", question.Content[..Math.Min(80, question.Content.Length)]);

        // ----------------------------------------
        // TRẠM 5: Question Mutation Engine
        // Sinh 2-3 câu hỏi biến thể đổi số liệu/ngữ cảnh
        // ----------------------------------------
        try
        {
            var variants = await _geminiService.MutateQuestionAsync(extracted, subject.Code, variantCount: 2, ct);
            foreach (var variant in variants)
            {
                var variantHash = ComputeSha256(variant.Content);
                float[]? variantEmbedding = null;
                try
                {
                    variantEmbedding = await _embeddingService.GetEmbeddingAsync(variant.Content, ct);
                }
                catch
                {
                    // Ignore embedding failure on mutation
                }
                var variantSource = $"{sourceName} (Biến thể AI)";
                var variantQuestion = MapToQuestion(variant, subject.Id, variantHash, variantEmbedding, variantSource, examYear);
                await _questionRepo.AddAsync(variantQuestion, ct);
                _logger.LogInformation("Sinh biến thể thành công: '{Content}'", variantQuestion.Content[..Math.Min(60, variantQuestion.Content.Length)]);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi khi sinh biến thể câu hỏi, tiếp tục quy trình");
        }

        return true;
    }

    /// <summary>
    /// Map ExtractedQuestion (từ Gemini / Rule-Based) -> Question Entity
    /// </summary>
    private static Question MapToQuestion(
        ExtractedQuestion extracted,
        Guid subjectId,
        string contentHash,
        float[]? embedding,
        string sourceName,
        short? examYear)
    {
        var format = Enum.Parse<QuestionFormat>(extracted.Format.Replace("_", ""), ignoreCase: true);
        var difficulty = Enum.Parse<DifficultyLevel>(extracted.Difficulty.Replace("_", ""), ignoreCase: true);
        var sourceTag = BuildSourceTag(sourceName, examYear);

        return new Question
        {
            Id = Guid.NewGuid(),
            SubjectId = subjectId,
            Content = extracted.Content,
            AttachedTable = extracted.AttachedTable,
            Format = format,
            Difficulty = difficulty,
            Topic = extracted.Topic,
            AnswerData = extracted.AnswerData,
            Explanation = extracted.Explanation,
            DistractorAnalysis = extracted.DistractorAnalysis ?? new(),
            ContentHash = contentHash,
            ContentEmbedding = (embedding is not null && embedding.Length > 0) ? new Vector(embedding) : null,
            SourceTags = [sourceTag],
            FirstSeenYear = examYear,
            OccurrenceCount = 1,
            IsVerified = false,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Tính mã băm SHA-256 của nội dung câu hỏi (chuẩn hóa whitespace trước)
    /// </summary>
    private static string ComputeSha256(string content)
    {
        // Chuẩn hóa: lowercase, trim, collapse whitespace
        var normalized = string.Join(' ', content.ToLowerInvariant().Split(
            [' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string BuildSourceTag(string sourceName, short? examYear)
        => examYear.HasValue ? $"{sourceName} {examYear}" : sourceName;
}
