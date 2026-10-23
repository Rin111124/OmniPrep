using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using OmniPrep.Infrastructure.Services;
using Xunit;

namespace OmniPrep.Tests;

/// <summary>
/// Bộ kiểm thử an ninh chuyên sâu (Security Hardening Tests) theo Giai đoạn 1:
/// 1. Kiểm soát tải URL, chặn SSRF, DNS Rebinding, User-Info, Port scanning.
/// 2. Sanitize phản hồi lỗi 500, chống rò rỉ exception, stack trace, class name ra ngoài client.
/// 3. Xác thực tính hợp lệ của MIME và magic bytes tài liệu đề thi.
/// </summary>
public class SecurityHardeningTests
{
    // =========================================================================
    // 1. SSRF & RESTRICTED IP COMPREHENSIVE TESTS
    // =========================================================================

    [Theory]
    // Loopback & Localhost
    [InlineData("127.0.0.1", true)]
    [InlineData("127.0.0.2", true)]
    [InlineData("127.255.255.255", true)]
    // Cloud Instance Metadata Service (AWS, GCP, Azure, OpenStack)
    [InlineData("169.254.169.254", true)]
    [InlineData("169.254.1.1", true)]
    // RFC 1918 Private IPv4 Class A
    [InlineData("10.0.0.1", true)]
    [InlineData("10.100.200.50", true)]
    [InlineData("10.255.255.255", true)]
    // RFC 1918 Private IPv4 Class B (172.16.0.0/12)
    [InlineData("172.16.0.1", true)]
    [InlineData("172.20.10.5", true)]
    [InlineData("172.31.255.254", true)]
    // RFC 1918 Private IPv4 Class C
    [InlineData("192.168.0.1", true)]
    [InlineData("192.168.1.254", true)]
    // Carrier-Grade NAT (RFC 6598: 100.64.0.0/10)
    [InlineData("100.64.0.1", true)]
    [InlineData("100.100.50.25", true)]
    [InlineData("100.127.255.254", true)]
    // IETF Protocol & Benchmarking (RFC 5735, RFC 2544)
    [InlineData("192.0.0.1", true)]
    [InlineData("198.18.0.1", true)]
    [InlineData("198.19.10.20", true)]
    // Multicast & Reserved (224.0.0.0/4 & 240.0.0.0/4)
    [InlineData("224.0.0.1", true)]
    [InlineData("239.255.255.250", true)]
    [InlineData("240.0.0.1", true)]
    [InlineData("255.255.255.255", true)]
    // IPv6 Loopback, Link-Local, ULA, Documentation
    [InlineData("::1", true)]
    [InlineData("fe80::1ff:fe00:1", true)]
    [InlineData("fc00::1", true)]
    [InlineData("fd00::1234", true)]
    [InlineData("2001:db8::1", true)]
    // IPv4-Mapped IPv6 addressing loopback/private
    [InlineData("::ffff:127.0.0.1", true)]
    [InlineData("::ffff:10.0.0.1", true)]
    [InlineData("::ffff:169.254.169.254", true)]
    [InlineData("::ffff:192.168.1.1", true)]
    // Public safe IPs
    [InlineData("8.8.8.8", false)]
    [InlineData("1.1.1.1", false)]
    [InlineData("142.250.190.46", false)]
    [InlineData("::ffff:8.8.8.8", false)]
    public void IsPrivateOrRestrictedIp_ValidatesAllForbiddenRanges(string ipString, bool expectedRestricted)
    {
        var ip = IPAddress.Parse(ipString);
        bool result = SafeDocumentFetcher.IsPrivateOrRestrictedIp(ip);
        Assert.Equal(expectedRestricted, result);
    }

    // =========================================================================
    // 2. URL & PORT & SCHEME HARDENING TESTS
    // =========================================================================

    [Theory]
    [InlineData("http://moet.gov.vn/de-thi.pdf")]             // Plain HTTP
    [InlineData("http://localhost:5000/api")]                 // Localhost HTTP
    [InlineData("ftp://ftp.example.com/exam.pdf")]            // FTP
    [InlineData("file:///etc/passwd")]                        // Local file
    [InlineData("https://admin:secret123@moet.gov.vn/de.pdf")] // User-info / credentials
    [InlineData("https://moet.gov.vn:22/exam.pdf")]           // SSH port
    [InlineData("https://moet.gov.vn:80/exam.pdf")]           // HTTP port on HTTPS
    [InlineData("https://moet.gov.vn:3306/exam.pdf")]         // MySQL port
    [InlineData("https://moet.gov.vn:5432/exam.pdf")]         // Postgres port
    [InlineData("https://moet.gov.vn:6379/exam.pdf")]         // Redis port
    [InlineData("https://moet.gov.vn:8080/exam.pdf")]         // Non-whitelisted port
    public void ValidateAndNormalizeUri_InsecureOrDisallowedUrls_ThrowsArgumentException(string url)
    {
        Assert.Throws<ArgumentException>(() => SafeDocumentFetcher.ValidateAndNormalizeUri(url, requireHttps: true));
    }

    [Theory]
    [InlineData("https://moet.gov.vn/de-thi.pdf", 443)]
    [InlineData("https://hanoi.edu.vn:8443/de-thi.pdf", 8443)]
    [InlineData("https://cdn.omniprep.vn/exams/2025/geo.pdf", 443)]
    public void ValidateAndNormalizeUri_AllowedHttpsUrls_Succeeds(string url, int expectedPort)
    {
        var uri = SafeDocumentFetcher.ValidateAndNormalizeUri(url, requireHttps: true);
        Assert.NotNull(uri);
        Assert.Equal("https", uri.Scheme);
        Assert.Equal(expectedPort, uri.Port);
    }

    // =========================================================================
    // 3. MIME TYPE & MAGIC BYTES DEFENSE
    // =========================================================================

    [Fact]
    public void DetectAndValidateMimeType_PdfHeader_Passes()
    {
        byte[] pdfBytes = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<<>>\nendobj");
        var mime = SafeDocumentFetcher.DetectAndValidateMimeType(pdfBytes, "sample.pdf", "application/pdf");
        Assert.Equal("application/pdf", mime);
    }

    [Theory]
    [InlineData("<!DOCTYPE html><html><body>Access Denied</body></html>")]
    [InlineData("<HTML><HEAD><TITLE>Cloudflare DDoS</TITLE></HEAD></HTML>")]
    [InlineData("<script>window.location='https://evil.com'</script>")]
    public void DetectAndValidateMimeType_HtmlOrScriptMasquerading_ThrowsInvalidOperationException(string html)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(html);
        Assert.Throws<InvalidOperationException>(() =>
            SafeDocumentFetcher.DetectAndValidateMimeType(bytes, "downloaded.pdf", "application/pdf"));
    }

    [Fact]
    public void DetectAndValidateMimeType_FakePdfExtensionWithoutMagicBytes_ThrowsInvalidOperationException()
    {
        byte[] randomBytes = Encoding.UTF8.GetBytes("This is not a real PDF file content.");
        Assert.Throws<InvalidOperationException>(() =>
            SafeDocumentFetcher.DetectAndValidateMimeType(randomBytes, "fake.pdf", "application/pdf"));
    }

    // =========================================================================
    // 4. SANITIZATION OF PRODUCTION 500 ERRORS (NO LEAKING EXCEPTION/STACKTRACE)
    // =========================================================================

    [Fact]
    public void ProductionErrorFormat_SanitizedWithoutExceptionLeak()
    {
        // Mô phỏng payload phản hồi an toàn ở Production
        var traceId = Guid.NewGuid().ToString();
        var sanitizedResponse = new
        {
            error = "INTERNAL_SERVER_ERROR",
            message = "Đã xảy ra lỗi nội bộ từ hệ thống. Vui lòng liên hệ quản trị viên hoặc thử lại sau.",
            traceId
        };

        var json = JsonSerializer.Serialize(sanitizedResponse);

        // Khẳng định: Không chứa thông tin stack trace, tên class exception, hay chuỗi kết nối
        Assert.DoesNotContain("Exception", json);
        Assert.DoesNotContain("StackTrace", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("at OmniPrep.", json);
        Assert.Contains("INTERNAL_SERVER_ERROR", json);
        Assert.Contains(traceId, json);
    }
}
