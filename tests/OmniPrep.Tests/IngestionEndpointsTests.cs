using System.Text;
using OmniPrep.Api.Endpoints;
using Xunit;

namespace OmniPrep.Tests;

public class IngestionEndpointsTests
{
    [Theory]
    // Trường hợp P0: 0 câu hỏi trích xuất được hoặc không có câu nào mới/gộp -> Phải báo Failed!
    [InlineData(0, 0, 0, 0, true)]
    // Tất cả câu hỏi trích xuất đều thất bại ở validator -> Failed
    [InlineData(10, 10, 0, 0, true)]
    // Trích xuất được nhưng không câu nào được nạp mới hoặc gộp -> Failed
    [InlineData(5, 0, 0, 0, true)]
    // Có câu mới được nạp thành công -> Không Failed (Completed)
    [InlineData(10, 2, 8, 0, false)]
    // Có câu trùng lặp được gộp thành công -> Không Failed (Completed)
    [InlineData(10, 0, 0, 10, false)]
    // Hỗn hợp câu mới và câu gộp -> Không Failed (Completed)
    [InlineData(10, 1, 5, 4, false)]
    public void DetermineIsFailed_EvaluatesCorrectStatus(
        int totalExtracted, int totalFailed, int totalNew, int totalMerged, bool expectedFailed)
    {
        var actual = IngestionEndpoints.DetermineIsFailed(totalExtracted, totalFailed, totalNew, totalMerged);
        Assert.Equal(expectedFailed, actual);
    }

    [Fact]
    public void TryDetectImageMime_ValidJpegSignature_ReturnsJpegMime()
    {
        byte[] jpegBytes = { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 };
        var ok = IngestionEndpoints.TryDetectImageMime(jpegBytes, ".jpg", out var mime);

        Assert.True(ok);
        Assert.Equal("image/jpeg", mime);
    }

    [Fact]
    public void TryDetectImageMime_ValidPngSignature_ReturnsPngMime()
    {
        byte[] pngBytes = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 };
        var ok = IngestionEndpoints.TryDetectImageMime(pngBytes, ".png", out var mime);

        Assert.True(ok);
        Assert.Equal("image/png", mime);
    }

    [Fact]
    public void TryDetectImageMime_ValidWebpSignature_ReturnsWebpMime()
    {
        // RIFF .... WEBP
        byte[] webpBytes = {
            0x52, 0x49, 0x46, 0x46,
            0x24, 0x00, 0x00, 0x00,
            0x57, 0x45, 0x42, 0x50,
            0x56, 0x50, 0x38, 0x20
        };
        var ok = IngestionEndpoints.TryDetectImageMime(webpBytes, ".webp", out var mime);

        Assert.True(ok);
        Assert.Equal("image/webp", mime);
    }

    [Fact]
    public void TryDetectImageMime_ValidBmpSignature_ReturnsBmpMime()
    {
        byte[] bmpBytes = { 0x42, 0x4D, 0x36, 0x00, 0x00, 0x00 };
        var ok = IngestionEndpoints.TryDetectImageMime(bmpBytes, ".bmp", out var mime);

        Assert.True(ok);
        Assert.Equal("image/bmp", mime);
    }

    [Fact]
    public void TryDetectImageMime_ValidPdfSignature_ReturnsPdfMime()
    {
        byte[] pdfBytes = Encoding.ASCII.GetBytes("%PDF-1.4\n%test");
        var ok = IngestionEndpoints.TryDetectImageMime(pdfBytes, ".pdf", out var mime);

        Assert.True(ok);
        Assert.Equal("application/pdf", mime);
    }

    [Fact]
    public void TryDetectImageMime_CorruptOrShortBytes_ReturnsFalse()
    {
        byte[] corruptBytes = { 0x00, 0x01 };
        var ok = IngestionEndpoints.TryDetectImageMime(corruptBytes, ".unknown", out var mime);

        Assert.False(ok);
    }

    [Fact]
    public void TryDetectImageMime_DisguisedScript_DoesNotMatchImageMimes()
    {
        byte[] htmlScript = Encoding.UTF8.GetBytes("<script>alert('xss')</script>");
        var ok = IngestionEndpoints.TryDetectImageMime(htmlScript, ".exe", out _);

        Assert.False(ok);
    }
}
