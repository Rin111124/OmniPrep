using System.Net;
using System.Text;
using OmniPrep.Domain.Enums;
using OmniPrep.Infrastructure.Services;
using Xunit;

namespace OmniPrep.Tests;

public class SafeDocumentFetcherTests
{
    [Theory]
    // Loopback IPv4
    [InlineData("127.0.0.1", true)]
    [InlineData("127.100.0.1", true)]
    // Private IPv4 Class A
    [InlineData("10.0.0.1", true)]
    [InlineData("10.254.254.254", true)]
    // Private IPv4 Class B (172.16.0.0 - 172.31.255.255)
    [InlineData("172.16.0.1", true)]
    [InlineData("172.24.1.1", true)]
    [InlineData("172.31.255.254", true)]
    // Public IPv4 near Class B
    [InlineData("172.15.255.255", false)]
    [InlineData("172.32.0.1", false)]
    // Private IPv4 Class C
    [InlineData("192.168.1.1", true)]
    [InlineData("192.168.100.50", true)]
    // Link-local & AWS Metadata
    [InlineData("169.254.169.254", true)]
    [InlineData("169.254.1.1", true)]
    // Multicast & Broadcast
    [InlineData("224.0.0.1", true)]
    [InlineData("255.255.255.255", true)]
    // Public DNS / Web IPs
    [InlineData("8.8.8.8", false)]
    [InlineData("1.1.1.1", false)]
    [InlineData("142.250.190.46", false)]
    // IPv6 Loopback & Link-local & ULA
    [InlineData("::1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("fc00::1", true)]
    [InlineData("fd12:3456:789a::1", true)]
    // IPv4-Mapped IPv6
    [InlineData("::ffff:127.0.0.1", true)]
    [InlineData("::ffff:192.168.1.1", true)]
    [InlineData("::ffff:8.8.8.8", false)]
    public void IsPrivateOrRestrictedIp_EvaluatesCorrectly(string ipString, bool expectedRestricted)
    {
        var ip = IPAddress.Parse(ipString);
        var actual = SafeDocumentFetcher.IsPrivateOrRestrictedIp(ip);
        Assert.Equal(expectedRestricted, actual);
    }

    [Theory]
    [InlineData("https://moet.gov.vn/de-thi.pdf")]
    [InlineData("http://hanoi.edu.vn:8080/de-thi.pdf")]
    public void ValidateAndNormalizeUri_ValidWebUrls_Succeeds(string url)
    {
        var uri = SafeDocumentFetcher.ValidateAndNormalizeUri(url);
        Assert.NotNull(uri);
        Assert.True(uri.Scheme == "https" || uri.Scheme == "http");
    }

    [Theory]
    [InlineData("ftp://moet.gov.vn/de-thi.pdf")]
    [InlineData("file:///C:/secrets.txt")]
    [InlineData("https://admin:pass@moet.gov.vn/de-thi.pdf")] // User-info forbidden
    [InlineData("https://moet.gov.vn:22/de-thi.pdf")]        // SSH port forbidden
    [InlineData("https://moet.gov.vn:3306/de-thi.pdf")]      // MySQL port forbidden
    public void ValidateAndNormalizeUri_MaliciousOrInvalidUrls_ThrowsArgumentException(string url)
    {
        Assert.Throws<ArgumentException>(() => SafeDocumentFetcher.ValidateAndNormalizeUri(url));
    }

    [Fact]
    public void DetectAndValidateMimeType_ValidPdfMagicBytes_ReturnsApplicationPdf()
    {
        byte[] pdfBytes = Encoding.ASCII.GetBytes("%PDF-1.7\n%stream");
        var mime = SafeDocumentFetcher.DetectAndValidateMimeType(pdfBytes, "exam.pdf", "application/pdf");
        Assert.Equal("application/pdf", mime);
    }

    [Theory]
    [InlineData("<!DOCTYPE html><html><body>Error 404</body></html>")]
    [InlineData("<html lang=\"vi\"><head><title>Login</title></head></html>")]
    [InlineData("<script>alert('xss')</script>")]
    public void DetectAndValidateMimeType_HtmlOrScriptMasqueradingAsDocument_ThrowsInvalidOperationException(string html)
    {
        byte[] htmlBytes = Encoding.UTF8.GetBytes(html);
        Assert.Throws<InvalidOperationException>(() =>
            SafeDocumentFetcher.DetectAndValidateMimeType(htmlBytes, "exam.pdf", "text/html"));
    }

    [Fact]
    public async Task CuratedSourcesDiscoveryProvider_FilterBySubject_ReturnsCuratedCandidatesWithProvenance()
    {
        var provider = new CuratedSourcesDiscoveryProvider();
        var candidates = await provider.DiscoverAsync(SubjectCode.GEO);

        Assert.NotEmpty(candidates);
        Assert.All(candidates, c =>
        {
            Assert.Equal(SubjectCode.GEO, c.SubjectCode);
            Assert.False(string.IsNullOrWhiteSpace(c.DownloadUrl));
            Assert.False(string.IsNullOrWhiteSpace(c.SourcePageUrl));
            Assert.False(string.IsNullOrWhiteSpace(c.SourceName));
            Assert.False(string.IsNullOrWhiteSpace(c.CuratedReason));
            Assert.False(string.IsNullOrWhiteSpace(c.PublishedDate));
        });
    }
}
