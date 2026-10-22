using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace OmniPrep.Infrastructure.Services;

public sealed record FetchedDocumentResult(
    byte[] Data,
    string ContentType,
    string DetectedMimeType,
    string FileName,
    string FinalUrl
);

public interface IExamDocumentFetcher
{
    Task<FetchedDocumentResult> FetchDocumentAsync(
        string url,
        long maxBytes = 10 * 1024 * 1024,
        CancellationToken ct = default);
}

/// <summary>
/// Dịch vụ tải tài liệu đề thi trực tuyến an toàn (P0 & P1 - online-exam-discovery-plan.md).
/// Bảo vệ chống SSRF, DNS Rebinding, lạm dụng redirect mạng nội bộ, và giới hạn stream RAM.
/// </summary>
public sealed class SafeDocumentFetcher : IExamDocumentFetcher
{
    private readonly ILogger<SafeDocumentFetcher> _logger;
    private const int MaxRedirectHops = 3;

    public SafeDocumentFetcher(ILogger<SafeDocumentFetcher> logger)
    {
        _logger = logger;
    }

    public async Task<FetchedDocumentResult> FetchDocumentAsync(
        string url,
        long maxBytes = 10 * 1024 * 1024,
        CancellationToken ct = default)
    {
        string currentUrl = url;
        int redirectCount = 0;

        while (true)
        {
            var uri = ValidateAndNormalizeUri(currentUrl);

            // Kiểm tra phân giải DNS và chặn mọi IP mạng nội bộ / private / loopback
            await ValidateDnsAddressesAsync(uri.Host, ct);

            using var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false, // Tự xử lý redirect để kiểm tra an ninh từng chặng
                ConnectTimeout = TimeSpan.FromSeconds(10),
                PooledConnectionLifetime = TimeSpan.FromMinutes(1)
            };

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("OmniPrep-ExamDiscoveryBot/1.0 (+https://omniprep.edu.vn)");

            _logger.LogInformation("Đang tải tài liệu an toàn từ URL: {Url}", uri);

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            // Xử lý chuyển hướng (Redirect 301, 302, 307, 308)
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                redirectCount++;
                if (redirectCount > MaxRedirectHops)
                {
                    throw new InvalidOperationException($"Vượt quá số lần chuyển hướng cho phép (tối đa {MaxRedirectHops} lần).");
                }

                var location = response.Headers.Location;
                if (location is null)
                {
                    throw new InvalidOperationException("Phản hồi chuyển hướng thiếu tiêu đề Location.");
                }

                // Nếu là relative URL thì resolve theo base uri
                var nextUri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                _logger.LogWarning("Chuyển hướng chặng {Count} tới: {NextUrl}", redirectCount, nextUri);
                currentUrl = nextUri.ToString();
                continue;
            }

            response.EnsureSuccessStatusCode();

            // Kiểm tra Content-Length nếu máy chủ có cung cấp
            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength.HasValue && contentLength.Value > maxBytes)
            {
                throw new InvalidOperationException($"Dung lượng tài liệu ({contentLength.Value / (1024 * 1024.0):F2} MB) vượt quá giới hạn cho phép ({maxBytes / (1024 * 1024)} MB).");
            }

            var contentType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? "application/octet-stream";

            // Đọc stream an toàn kèm giới hạn byte tối đa (Bảo vệ chống cạn kiệt RAM khi Chunked)
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var ms = new MemoryStream();
            var buffer = new byte[8192];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
            {
                totalRead += bytesRead;
                if (totalRead > maxBytes)
                {
                    throw new InvalidOperationException($"Tài liệu tải về vượt quá giới hạn tối đa {maxBytes / (1024 * 1024)} MB khi đọc stream dữ liệu.");
                }
                ms.Write(buffer, 0, bytesRead);
            }

            var data = ms.ToArray();
            if (data.Length == 0)
            {
                throw new InvalidOperationException("Nội dung tài liệu từ URL bị rỗng.");
            }

            // Kiểm tra chữ ký (Magic Bytes) và định dạng nội dung
            var detectedMime = DetectAndValidateMimeType(data, uri.LocalPath, contentType);

            string fileName = Path.GetFileName(uri.LocalPath);
            if (string.IsNullOrWhiteSpace(fileName) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                fileName = detectedMime == "application/pdf" ? "document.pdf" : "document.txt";
            }

            _logger.LogInformation("Tải tài liệu thành công: {Bytes} bytes, MIME: {Mime}, Tên file: {FileName}", data.Length, detectedMime, fileName);

            return new FetchedDocumentResult(
                Data: data,
                ContentType: contentType,
                DetectedMimeType: detectedMime,
                FileName: fileName,
                FinalUrl: uri.ToString()
            );
        }
    }

    /// <summary>
    /// Kiểm tra tính hợp lệ và chuẩn hóa URI đầu vào
    /// </summary>
    public static Uri ValidateAndNormalizeUri(string rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl) || !Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException("Đường dẫn URL không hợp lệ.");
        }

        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
        {
            throw new ArgumentException("Hệ thống chỉ chấp nhận giao thức web HTTPS hoặc HTTP.");
        }

        // Chặn user-info (ví dụ https://user:pass@evil.com/)
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ArgumentException("URL không được phép chứa thông tin xác thực user-info.");
        }

        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new ArgumentException("URL thiếu tên máy chủ (Host).");
        }

        // Chỉ cho phép các port web chuẩn
        if (uri.Port != -1 && uri.Port != 80 && uri.Port != 443 && uri.Port != 8080 && uri.Port != 8443)
        {
            throw new ArgumentException($"Cổng kết nối {uri.Port} không nằm trong danh sách được phép.");
        }

        return uri;
    }

    /// <summary>
    /// Phân giải DNS và kiểm tra chống SSRF toàn diện
    /// </summary>
    public static async Task ValidateDnsAddressesAsync(string host, CancellationToken ct)
    {
        IPAddress[] addresses;
        if (IPAddress.TryParse(host, out var directIp))
        {
            addresses = [directIp];
        }
        else
        {
            try
            {
                addresses = await Dns.GetHostAddressesAsync(host, ct);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Không thể phân giải tên miền '{host}': {ex.Message}", ex);
            }
        }

        if (addresses.Length == 0)
        {
            throw new InvalidOperationException($"Không tìm thấy địa chỉ IP cho máy chủ '{host}'.");
        }

        foreach (var ip in addresses)
        {
            if (IsPrivateOrRestrictedIp(ip))
            {
                throw new InvalidOperationException($"Địa chỉ IP '{ip}' của máy chủ '{host}' bị chặn vì thuộc mạng nội bộ hoặc dải địa chỉ cấm (SSRF Protection).");
            }
        }
    }

    /// <summary>
    /// Kiểm tra xem địa chỉ IP có thuộc mạng private, loopback, link-local hoặc dải cấm hay không
    /// </summary>
    public static bool IsPrivateOrRestrictedIp(IPAddress ip)
    {
        // Xử lý trường hợp IPv4-mapped IPv6 (ví dụ ::ffff:127.0.0.1)
        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        if (IPAddress.IsLoopback(ip)) return true;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();
            byte b0 = bytes[0];
            byte b1 = bytes[1];

            // 0.0.0.0/8 (Mạng hiện tại)
            if (b0 == 0) return true;

            // 10.0.0.0/8 (Private Network)
            if (b0 == 10) return true;

            // 127.0.0.0/8 (Loopback)
            if (b0 == 127) return true;

            // 169.254.0.0/16 (Link-local & AWS/Cloud Metadata 169.254.169.254)
            if (b0 == 169 && b1 == 254) return true;

            // 172.16.0.0/12 (Private Network: 172.16.x.x -> 172.31.x.x)
            if (b0 == 172 && (b1 >= 16 && b1 <= 31)) return true;

            // 192.168.0.0/16 (Private Network)
            if (b0 == 192 && b1 == 168) return true;

            // 224.0.0.0/4 (Multicast)
            if (b0 >= 224 && b0 <= 239) return true;

            // 240.0.0.0/4 (Reserved / Future use)
            if (b0 >= 240) return true;

            // 255.255.255.255 (Broadcast)
            if (b0 == 255 && b1 == 255 && bytes[2] == 255 && bytes[3] == 255) return true;

            return false;
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // ::1 (Loopback)
            if (ip.Equals(IPAddress.IPv6Loopback) || ip.Equals(IPAddress.IPv6None) || ip.Equals(IPAddress.IPv6Any))
                return true;

            // fe80::/10 (Link-local)
            if (ip.IsIPv6LinkLocal) return true;

            // ff00::/8 (Multicast)
            if (ip.IsIPv6Multicast) return true;

            var bytes = ip.GetAddressBytes();
            // fc00::/7 (Unique Local Address - ULA)
            if ((bytes[0] & 0xFE) == 0xFC) return true;

            return false;
        }

        return true;
    }

    /// <summary>
    /// Nhận diện MIME dựa trên chữ ký nhị phân (Magic Bytes), từ chối HTML script/bot trá hình
    /// </summary>
    public static string DetectAndValidateMimeType(byte[] bytes, string localPath, string declaredContentType)
    {
        // Chữ ký PDF: %PDF- (0x25, 0x50, 0x44, 0x46, 0x2D)
        if (bytes.Length >= 5 && bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46 && bytes[4] == 0x2D)
        {
            return "application/pdf";
        }

        // Kiểm tra nếu nội dung bắt đầu bằng HTML (do server trả về trang lỗi 404/login/captcha dạng 200 OK)
        string preview = Encoding.UTF8.GetString(bytes.Take(Math.Min(bytes.Length, 1024)).ToArray()).Trim();
        if (preview.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) ||
            preview.StartsWith("<html", StringComparison.OrdinalIgnoreCase) ||
            preview.StartsWith("<head", StringComparison.OrdinalIgnoreCase) ||
            preview.StartsWith("<script", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("URL trả về trang web HTML hoặc mã script thay vì tập tin đề thi (PDF/TXT). Vui lòng cung cấp liên kết tải trực tiếp.");
        }

        if (localPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) || declaredContentType.Contains("pdf"))
        {
            throw new InvalidOperationException("Tập tin được khai báo là PDF nhưng không có chữ ký file PDF (%PDF-) hợp lệ.");
        }

        // Mặc định là plain text hợp lệ
        return "text/plain";
    }
}
