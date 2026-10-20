using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using OmniPrep.Infrastructure.Data;
using OmniPrep.Infrastructure.Services;

namespace OmniPrep.Api.Endpoints;

// ============================================================
// HEALTH - Kiểm tra kết nối Database & Gemini
// ============================================================
public static class HealthEndpoints
{
    public static RouteGroupBuilder MapHealthApi(this RouteGroupBuilder group)
    {
        group.MapGet("/health", async (
            OmniPrepDbContext db,
            IOptions<GeminiOptions> geminiOptions,
            CancellationToken ct) =>
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

            return Results.Ok(new
            {
                Status = dbConnected && geminiConfigured ? "Healthy" : "PartiallyConfigured",
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
            });
        }).WithName("HealthCheck").AllowAnonymous().WithTags("System");

        return group;
    }
}
