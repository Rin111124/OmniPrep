using System.Text;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;

namespace OmniPrep.Infrastructure.Services;

/// <summary>
/// Giao diện trích xuất văn bản từ tài liệu PDF an toàn (Mục 5 & P2.1 Lộ trình)
/// </summary>
public interface IPdfDocumentExtractor
{
    Task<string> ExtractTextAsync(Stream pdfStream, CancellationToken ct = default);
}

public sealed class PdfDocumentExtractor : IPdfDocumentExtractor
{
    private readonly ILogger<PdfDocumentExtractor> _logger;

    public PdfDocumentExtractor(ILogger<PdfDocumentExtractor> logger)
    {
        _logger = logger;
    }

    public Task<string> ExtractTextAsync(Stream pdfStream, CancellationToken ct = default)
    {
        try
        {
            using var memoryStream = new MemoryStream();
            pdfStream.CopyTo(memoryStream);
            memoryStream.Position = 0;

            using var document = PdfDocument.Open(memoryStream);
            var sb = new StringBuilder();
            int pageCount = document.NumberOfPages;

            _logger.LogInformation("Bắt đầu trích xuất văn bản từ file PDF ({Pages} trang)", pageCount);

            foreach (var page in document.GetPages())
            {
                ct.ThrowIfCancellationRequested();
                var text = page.Text;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    sb.AppendLine($"\n--- TRANG {page.Number} ---");
                    sb.AppendLine(text);
                }
            }

            var result = sb.ToString().Trim();
            _logger.LogInformation("Trích xuất PDF hoàn tất: {Length} ký tự từ {Pages} trang", result.Length, pageCount);
            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi giải mã và trích xuất text từ tài liệu PDF");
            throw new InvalidOperationException($"Không thể trích xuất văn bản từ tài liệu PDF: {ex.Message}", ex);
        }
    }
}
