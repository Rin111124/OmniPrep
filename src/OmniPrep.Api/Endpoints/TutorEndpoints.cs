using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniPrep.Domain.Interfaces;
using OmniPrep.Domain.ValueObjects;
using OmniPrep.Infrastructure.Services;

namespace OmniPrep.Api.Endpoints;

// ============================================================
// AI TUTOR - Giải thích lỗi sai qua Streaming
// ============================================================
public static class TutorEndpoints
{
    public static RouteGroupBuilder MapTutorApi(this RouteGroupBuilder group)
    {
        var tutor = group.MapGroup("/tutor").WithTags("AI Tutor");

        /// <summary>
        /// Endpoint chat streaming trực tiếp với Gia sư AI (Server-Sent Events)
        /// Phục vụ cho Drawer trên frontend khi học sinh hỏi về câu sai
        /// </summary>
        tutor.MapPost("/chat-stream", async (
            TutorContext context,
            IGeminiService geminiService,
            IOptions<GeminiOptions> geminiOptions,
            ILoggerFactory loggerFactory,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("OmniPrep.Api.Tutor");
            var apiKey = geminiOptions.Value.ApiKey;
            if (string.IsNullOrWhiteSpace(apiKey) || apiKey == "YOUR_GEMINI_API_KEY")
                return Results.Problem(title: "AI tutor unavailable", statusCode: StatusCodes.Status503ServiceUnavailable);

            httpContext.Response.Headers.ContentType = "text/event-stream";
            httpContext.Response.Headers.CacheControl = "no-cache";
            httpContext.Response.Headers.Connection = "keep-alive";
            try
            {
                await foreach (var chunk in geminiService.GetTutorExplanationStreamAsync(context, ct))
                {
                    var normalizedChunk = chunk.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
                    foreach (var line in normalizedChunk.Split('\n'))
                        await httpContext.Response.WriteAsync($"data: {line}\n", ct);
                    await httpContext.Response.WriteAsync("\n", ct);
                    await httpContext.Response.Body.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return Results.Empty;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "AI tutor stream failed");
                await httpContext.Response.WriteAsync("event: error\ndata: AI tutor is temporarily unavailable.\n\n", ct);
            }

            await httpContext.Response.WriteAsync("data: [DONE]\n\n", ct);
            return Results.Empty;
        }).WithName("ChatTutorStream").RequireAuthorization().RequireRateLimiting("tutor-per-user");

        return group;
    }
}
