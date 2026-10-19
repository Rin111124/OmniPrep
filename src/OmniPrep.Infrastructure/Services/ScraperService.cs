using Microsoft.Extensions.Logging;
using OmniPrep.Domain.Enums;
using OmniPrep.Domain.Interfaces;

namespace OmniPrep.Infrastructure.Services;

/// <summary>
/// Reads explicitly supplied text documents from the local drop folder.
/// Remote exam source crawling and PDF/DOCX extraction are not implemented.
/// </summary>
public sealed class ScraperService : IScraperService
{
    private const long MaxDocumentBytes = 2 * 1024 * 1024;
    private readonly ILogger<ScraperService> _logger;

    public ScraperService(HttpClient _, ILogger<ScraperService> logger)
    {
        _logger = logger;
    }

    public async Task<IReadOnlyList<ScrapedDocument>> ScrapeLatestDocumentsAsync(
        SubjectCode subjectCode,
        int maxFiles = 5,
        CancellationToken ct = default)
    {
        if (maxFiles <= 0) return [];

        var dropDirectory = Path.Combine(Directory.GetCurrentDirectory(), "scratch", "incoming_papers");
        if (!Directory.Exists(dropDirectory))
        {
            _logger.LogInformation("No local ingestion folder found at {DropDirectory}; remote sources are not configured.", dropDirectory);
            return [];
        }

        var documents = new List<ScrapedDocument>();
        foreach (var path in Directory.EnumerateFiles(dropDirectory, "*", SearchOption.TopDirectoryOnly)
                     .Where(path => Path.GetExtension(path).Equals(".txt", StringComparison.OrdinalIgnoreCase)
                         || Path.GetExtension(path).Equals(".md", StringComparison.OrdinalIgnoreCase))
                     .Take(maxFiles))
        {
            ct.ThrowIfCancellationRequested();
            var file = new FileInfo(path);
            if (file.Length == 0 || file.Length > MaxDocumentBytes)
            {
                _logger.LogWarning("Skipping local ingestion file with unsupported size: {FileName}", file.Name);
                continue;
            }

            try
            {
                var content = await File.ReadAllTextAsync(path, ct);
                if (string.IsNullOrWhiteSpace(content)) continue;

                documents.Add(new ScrapedDocument(
                    Title: file.Name,
                    SourceName: "Local drop folder",
                    ExamYear: null,
                    SubjectCode: subjectCode,
                    FileUrl: null,
                    ContentBytes: null,
                    TextContent: content));
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Could not read local ingestion file {FileName}", file.Name);
            }
        }

        return documents;
    }
}
