using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using OmniPrep.Infrastructure.Data;
using OmniPrep.Infrastructure.Services;

namespace OmniPrep.Api.Endpoints;

// ============================================================
// HEALTH - Kiểm tra Liveness & Readiness (Database & Gemini)
// ============================================================
public static class HealthEndpoints
{
    public static RouteGroupBuilder MapHealthApi(this RouteGroupBuilder group)
    {
        // 1. LIVENESS PROBE: Xác định API process đang sống (không bị treo)
        group.MapGet("/health/live", () => Results.Ok(new
        {
            Status = "Live",
            Timestamp = DateTimeOffset.UtcNow
        })).WithName("LivenessCheck").AllowAnonymous().WithTags("System");

        // 2. READINESS PROBE: Xác định API đã sẵn sàng nhận traffic (DB & Gemini)
        group.MapGet("/health/ready", async (
            OmniPrepDbContext db,
            IOptions<GeminiOptions> geminiOptions,
            CancellationToken ct) =>
        {
            var (_, statusCode, details) = await CheckReadinessAsync(db, geminiOptions, ct);
            return Results.Json(details, statusCode: statusCode);
        }).WithName("ReadinessCheck").AllowAnonymous().WithTags("System");

        // 3. BACKWARD COMPATIBILITY: /health giữ tương thích ngược,
        // phản ánh trạng thái readiness và trả về HTTP 503 nếu dịch vụ thiết yếu chưa sẵn sàng.
        group.MapGet("/health", async (
            OmniPrepDbContext db,
            IOptions<GeminiOptions> geminiOptions,
            CancellationToken ct) =>
        {
            var (_, statusCode, details) = await CheckReadinessAsync(db, geminiOptions, ct);
            return Results.Json(details, statusCode: statusCode);
        }).WithName("HealthCheck").AllowAnonymous().WithTags("System");

        return group;
    }

    public static async Task<(bool IsHealthy, int StatusCode, object Details)> CheckReadinessAsync(
        OmniPrepDbContext db,
        IOptions<GeminiOptions> geminiOptions,
        CancellationToken ct)
    {
        var dbConnected = false;
        try
        {
            dbConnected = await db.Database.CanConnectAsync(ct);
        }
        catch (Exception)
        {
            dbConnected = false;
        }

        var geminiConfigured = !string.IsNullOrWhiteSpace(geminiOptions.Value.ApiKey)
            && geminiOptions.Value.ApiKey != "YOUR_GEMINI_API_KEY";

        var isHealthy = dbConnected && geminiConfigured;
        var status = !dbConnected
            ? "Unhealthy"
            : (!geminiConfigured ? "Degraded" : "Healthy");

        // Trả về HTTP 503 khi database mất kết nối hoặc cấu hình thiết yếu chưa sẵn sàng
        var statusCode = isHealthy
            ? StatusCodes.Status200OK
            : StatusCodes.Status503ServiceUnavailable;

        var details = new
        {
            Status = status,
            Timestamp = DateTimeOffset.UtcNow,
            Database = new
            {
                Connected = dbConnected
            },
            Gemini = new
            {
                Configured = geminiConfigured,
                Model = geminiOptions.Value.GenerativeModel,
                EmbeddingModel = geminiOptions.Value.EmbeddingModel
            }
        };

        return (isHealthy, statusCode, details);
    }
}
