using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniPrep.Domain.Enums;
using OmniPrep.Domain.Interfaces;
using OmniPrep.Domain.ValueObjects;

namespace OmniPrep.Infrastructure.Services;

// ============================================================
// OmniPrep.Infrastructure - Gemini API Service
// Tích hợp Google Gemini 2.5 Flash cho:
// 1. Bóc tách câu hỏi từ PDF/Word (responseSchema)
// 2. AI Tutor giải thích lỗi sai (Streaming)
// 3. Tạo vector embedding (text-embedding-004)
// ============================================================

public class GeminiOptions
{
    public const string SectionName = "Gemini";
    public string ApiKey { get; set; } = string.Empty;
    /// <summary>Model cho bóc tách và AI Tutor</summary>
    public string GenerativeModel { get; set; } = "gemini-2.0-flash";
    /// <summary>Model cho vector embedding</summary>
    public string EmbeddingModel { get; set; } = "text-embedding-004";
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";
}

/// <summary>
/// Triển khai Gemini API cho bóc tách đề thi và AI Tutor.
/// Sử dụng Structured Output (responseSchema) để đảm bảo JSON nhất quán.
/// </summary>
public sealed class GeminiService : IGeminiService
{
    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _options;
    private readonly ISubjectStrategyFactory _strategyFactory;
    private readonly ILogger<GeminiService> _logger;

    // JSON Schema cho response bóc tách câu hỏi (Structured Output)
    private static readonly JsonElement QuestionExtractionSchema = JsonSerializer.Deserialize<JsonElement>("""
    {
        "type": "array",
        "items": {
            "type": "object",
            "properties": {
                "content": {"type": "string", "description": "Nội dung câu hỏi đầy đủ (Markdown)"},
                "attached_table": {"type": "string", "description": "Bảng số liệu nếu có (Markdown table)"},
                "format": {"type": "string", "enum": ["MULTIPLE_CHOICE", "TRUE_FALSE", "SHORT_ANSWER"]},
                "difficulty": {"type": "string", "enum": ["NHAN_BIET", "THONG_HIEU", "VAN_DUNG", "VAN_DUNG_CAO"]},
                "topic": {"type": "string", "description": "Chuyên đề chính xác"},
                "answer_data": {
                    "type": "object",
                    "description": "Dữ liệu đáp án theo format câu hỏi"
                },
                "explanation": {"type": "string", "description": "Lời giải chi tiết (Markdown)"},
                "distractor_analysis": {
                    "type": "object",
                    "description": "Phân tích lý do học sinh hay nhầm: {B: 'lý do', C: 'lý do'}"
                }
            },
            "required": ["content", "format", "difficulty", "topic", "answer_data"]
        }
    }
    """);

    public GeminiService(
        HttpClient httpClient,
        IOptions<GeminiOptions> options,
        ISubjectStrategyFactory strategyFactory,
        ILogger<GeminiService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _strategyFactory = strategyFactory;
        _logger = logger;
    }

    /// <summary>
    /// Bóc tách câu hỏi từ nội dung document sử dụng Gemini Structured Output.
    /// </summary>
    public async Task<IReadOnlyList<ExtractedQuestion>> ExtractQuestionsAsync(
        string documentContent,
        SubjectCode subjectCode,
        CancellationToken ct = default)
    {
        var strategy = _strategyFactory.GetStrategy(subjectCode);
        var prompt = strategy.BuildExtractionPrompt(documentContent);

        var requestBody = new
        {
            contents = new[]
            {
                new { role = "user", parts = new[] { new { text = prompt } } }
            },
            generationConfig = new
            {
                temperature = 0.1,          // Thấp để tăng tính nhất quán
                maxOutputTokens = 8192,     // Hỗ trợ bóc tách đề dài 40-50 câu hỏi mà không bị cắt cụt JSON
                responseMimeType = "application/json",
                responseSchema = QuestionExtractionSchema
            },
            systemInstruction = new
            {
                parts = new[] { new { text = "Bạn là chuyên gia giáo dục Việt Nam, chuyên bóc tách đề thi THPT. Luôn trả về JSON hợp lệ theo schema được chỉ định." } }
            }
        };

        var url = $"{_options.BaseUrl}/models/{_options.GenerativeModel}:generateContent?key={_options.ApiKey}";

        _logger.LogInformation("Gọi Gemini API bóc tách đề - Model: {Model}", _options.GenerativeModel);

        var response = await _httpClient.PostAsJsonAsync(url, requestBody, ct);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(ct);

        // Parse Gemini response structure
        using var doc = JsonDocument.Parse(responseJson);
        var text = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString() ?? "[]";

        var extracted = JsonSerializer.Deserialize<List<ExtractedQuestion>>(text,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        _logger.LogInformation("Gemini trả về {Count} câu hỏi", extracted?.Count ?? 0);
        return extracted ?? [];
    }

    /// <summary>
    /// Gọi AI Tutor giải thích lỗi sai theo chế độ Streaming.
    /// Sử dụng Server-Sent Events (streamGenerateContent).
    /// </summary>
    public async IAsyncEnumerable<string> GetTutorExplanationStreamAsync(
        TutorContext context,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var prompt = BuildTutorPrompt(context);

        var requestBody = new
        {
            contents = new[]
            {
                new { role = "user", parts = new[] { new { text = prompt } } }
            },
            generationConfig = new
            {
                temperature = 0.7,  // Cao hơn để câu giải thích tự nhiên, sinh động hơn
                maxOutputTokens = 1024
            },
            systemInstruction = new
            {
                parts = new[]
                {
                    new { text = $"""
                    Bạn là Gia sư AI thông minh, nhiệt tình và kiên nhẫn, chuyên giúp học sinh lớp 12 Việt Nam ôn luyện thi THPT.
                    Phong cách: Thân thiện, dùng tiếng Việt, giải thích rõ ràng từng bước, không phán xét lỗi sai của học sinh.
                    Luôn kết thúc bằng một câu hỏi gợi mở để kiểm tra lại kiến thức của học sinh.
                    """ }
                }
            }
        };

        var url = $"{_options.BaseUrl}/models/{_options.GenerativeModel}:streamGenerateContent?key={_options.ApiKey}&alt=sse";

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json")
        };

        using var response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new System.IO.StreamReader(stream);

        // Đọc SSE stream và yield từng chunk text
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (ct.IsCancellationRequested) break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!line.StartsWith("data: ")) continue;

            var data = line["data: ".Length..].Trim();
            if (data == "[DONE]") break;

            JsonElement chunkElement;
            try
            {
                using var chunkDoc = JsonDocument.Parse(data);
                chunkElement = chunkDoc.RootElement.Clone();
            }
            catch (JsonException)
            {
                continue;
            }

            var text = chunkElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();

            if (!string.IsNullOrEmpty(text))
                yield return text;
        }
    }

    /// <summary>
    /// Trạm 5: Question Mutation Engine
    /// Sinh 2-3 câu hỏi biến thể từ câu hỏi gốc mới để nhân rộng ngân hàng đề thi.
    /// Giữ nguyên dạng thức, chủ đề, mức độ nhận thức nhưng thay đổi số liệu/ngữ cảnh.
    /// </summary>
    public async Task<IReadOnlyList<ExtractedQuestion>> MutateQuestionAsync(
        ExtractedQuestion originalQuestion,
        SubjectCode subjectCode,
        int variantCount = 2,
        CancellationToken ct = default)
    {
        var originalJson = JsonSerializer.Serialize(originalQuestion, new JsonSerializerOptions { WriteIndented = true });

        var mutationPrompt = $"""
Bạn là chuyên gia khảo thí ra đề thi Tốt nghiệp THPT môn {subjectCode} của Bộ Giáo dục & Đào tạo Việt Nam.
Dưới đây là một câu hỏi gốc đã được chuẩn hóa:
```json
{originalJson}
```

Nhiệm vụ của bạn: Hãy tạo ra chính xác {variantCount} câu hỏi biến thể (variants) từ câu hỏi gốc trên.
Yêu cầu bắt buộc:
1. Giữ nguyên định dạng câu hỏi (format: {originalQuestion.Format}), chủ đề (topic: {originalQuestion.Topic}), và mức độ nhận thức (difficulty: {originalQuestion.Difficulty}).
2. THAY ĐỔI số liệu số học, bảng thống kê hoặc đối tượng ngữ cảnh sao cho học sinh phải áp dụng cùng phương pháp tư duy nhưng không thể học vẹt đáp án câu gốc.
3. Đảm bảo tính chính xác khoa học tuyệt đối, tính toán logic và đáp án đúng duy nhất.
4. Sinh kèm explanation giải thích chi tiết và distractor_analysis chỉ rõ bẫy đề thi cho từng biến thể.

Trả về danh sách {variantCount} câu hỏi dưới dạng mảng JSON tuân thủ schema.
""";

        var requestBody = new
        {
            contents = new[]
            {
                new { role = "user", parts = new[] { new { text = mutationPrompt } } }
            },
            generationConfig = new
            {
                temperature = 0.4,
                responseMimeType = "application/json",
                responseSchema = QuestionExtractionSchema
            },
            systemInstruction = new
            {
                parts = new[] { new { text = "Bạn là chuyên gia ra đề thi THPT Quốc gia Việt Nam. Luôn tạo câu hỏi biến thể chất lượng cao và trả về mảng JSON theo schema." } }
            }
        };

        var url = $"{_options.BaseUrl}/models/{_options.GenerativeModel}:generateContent?key={_options.ApiKey}";

        _logger.LogInformation("Gọi Gemini Mutation sinh {Count} biến thể cho câu: '{Content}'",
            variantCount, originalQuestion.Content[..Math.Min(50, originalQuestion.Content.Length)]);

        try
        {
            var response = await _httpClient.PostAsJsonAsync(url, requestBody, ct);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(responseJson);
            var text = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString() ?? "[]";

            var variants = JsonSerializer.Deserialize<List<ExtractedQuestion>>(text,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            _logger.LogInformation("Đã sinh thành công {Count} câu biến thể", variants?.Count ?? 0);
            return variants ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi khi sinh câu biến thể bằng Gemini, bỏ qua mutation cho câu này.");
            return [];
        }
    }

    /// <summary>
    /// Xây dựng prompt cho AI Tutor với đầy đủ context cá nhân hóa.
    /// </summary>
    private static string BuildTutorPrompt(TutorContext context)
    {
        var masteryPercent = (context.StudentMasteryRate * 100).ToString("F0");

        var sb = new StringBuilder();
        sb.AppendLine($"Hãy giúp {context.StudentName} hiểu tại sao mình làm sai câu hỏi này.");
        sb.AppendLine();
        sb.AppendLine($"**📚 Chuyên đề:** {context.Topic}");
        sb.AppendLine($"**📊 Tỷ lệ làm đúng của {context.StudentName} ở chuyên đề này:** {masteryPercent}%");
        sb.AppendLine();
        sb.AppendLine("**❓ Câu hỏi:**");
        sb.AppendLine(context.QuestionContent);

        if (!string.IsNullOrEmpty(context.AttachedTable))
        {
            sb.AppendLine();
            sb.AppendLine("**📋 Bảng số liệu:**");
            sb.AppendLine(context.AttachedTable);
        }

        sb.AppendLine();
        sb.AppendLine($"**✅ Đáp án đúng:** {FormatAnswer(context.CorrectAnswer)}");
        sb.AppendLine($"**❌ {context.StudentName} đã chọn:** {FormatAnswer(context.StudentAnswer)}");

        if (!string.IsNullOrEmpty(context.OfficialExplanation))
        {
            sb.AppendLine();
            sb.AppendLine("**📖 Lời giải chuẩn:**");
            sb.AppendLine(context.OfficialExplanation);
        }

        if (context.DistractorAnalysis?.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**🔍 Thông tin về các đáp án sai (để hỗ trợ giải thích):**");
            foreach (var (key, reason) in context.DistractorAnalysis)
                sb.AppendLine($"- {key}: {reason}");
        }

        sb.AppendLine();
        sb.AppendLine("Hãy:");
        sb.AppendLine($"1. Giải thích nhẹ nhàng tại sao đáp án {context.StudentName} chọn là sai");
        sb.AppendLine("2. Hướng dẫn cách tiếp cận đúng bước từng bước");
        sb.AppendLine("3. Nêu điểm cần ghi nhớ (trick hoặc kiến thức cốt lõi)");
        sb.AppendLine("4. Kết thúc bằng một câu hỏi nhỏ để kiểm tra lại hiểu biết");

        return sb.ToString();
    }

    private static string FormatAnswer(object? answer)
    {
        if (answer is null) return "*(Bỏ qua)*";
        if (answer is string s) return s;
        return JsonSerializer.Serialize(answer);
    }
}

/// <summary>
/// Triển khai Embedding Service sử dụng text-embedding-004.
/// Vector 768 chiều cho semantic similarity search.
/// </summary>
public sealed class EmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _options;
    private readonly ILogger<EmbeddingService> _logger;

    public EmbeddingService(
        HttpClient httpClient,
        IOptions<GeminiOptions> options,
        ILogger<EmbeddingService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default)
    {
        var requestBody = new
        {
            model = $"models/{_options.EmbeddingModel}",
            content = new { parts = new[] { new { text } } },
            taskType = "SEMANTIC_SIMILARITY"
        };

        var url = $"{_options.BaseUrl}/models/{_options.EmbeddingModel}:embedContent?key={_options.ApiKey}";

        var response = await _httpClient.PostAsJsonAsync(url, requestBody, ct);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var values = doc.RootElement
            .GetProperty("embedding")
            .GetProperty("values");

        var embedding = new float[values.GetArrayLength()];
        int i = 0;
        foreach (var val in values.EnumerateArray())
            embedding[i++] = val.GetSingle();

        _logger.LogDebug("Tạo embedding {Dims} chiều cho text {Length} ký tự",
            embedding.Length, text.Length);

        return embedding;
    }
}

/// <summary>
/// Factory đăng ký và cung cấp ISubjectStrategy theo SubjectCode.
/// Tuân thủ Open/Closed Principle - thêm môn mới không cần sửa code cũ.
/// </summary>
public sealed class SubjectStrategyFactory : ISubjectStrategyFactory
{
    private readonly IReadOnlyDictionary<SubjectCode, ISubjectStrategy> _strategies;

    public SubjectStrategyFactory(IEnumerable<ISubjectStrategy> strategies)
    {
        _strategies = strategies.ToDictionary(s => s.SubjectCode);
    }

    public ISubjectStrategy GetStrategy(SubjectCode subjectCode)
    {
        if (_strategies.TryGetValue(subjectCode, out var strategy))
            return strategy;

        throw new NotSupportedException(
            $"Chưa có Strategy cho môn học: {subjectCode}. " +
            $"Hãy implement ISubjectStrategy và đăng ký vào DI container.");
    }
}
