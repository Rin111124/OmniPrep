using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OmniPrep.Api.Endpoints;
using OmniPrep.Infrastructure.Data;
using OmniPrep.Infrastructure.Services;
using Xunit;

namespace OmniPrep.Tests;

public class HealthEndpointsTests
{
    [Fact]
    public async Task CheckReadinessAsync_WhenDatabaseCannotConnect_Returns503AndUnhealthy()
    {
        // Sử dụng connection string không thể kết nối với timeout cực ngắn (1s)
        var options = new DbContextOptionsBuilder<OmniPrepDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=54329;Database=fake;Username=u;Password=p;Timeout=1;CommandTimeout=1")
            .Options;
        using var db = new OmniPrepDbContext(options);

        var geminiOptions = Options.Create(new GeminiOptions
        {
            ApiKey = "valid-test-key",
            GenerativeModel = "gemini-2.0-flash",
            EmbeddingModel = "text-embedding-004"
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var (isHealthy, statusCode, details) = await HealthEndpoints.CheckReadinessAsync(db, geminiOptions, cts.Token);

        Assert.False(isHealthy);
        Assert.Equal(503, statusCode);

        dynamic json = details;
        Assert.Equal("Unhealthy", (string)json.Status);
        Assert.False((bool)json.Database.Connected);
        Assert.True((bool)json.Gemini.Configured);
    }

    [Fact]
    public async Task CheckReadinessAsync_WhenGeminiNotConfigured_Returns503AndDegraded()
    {
        // Trường hợp API Key trống hoặc là placeholder
        var geminiOptionsMissing = Options.Create(new GeminiOptions
        {
            ApiKey = "",
            GenerativeModel = "gemini-2.0-flash",
            EmbeddingModel = "text-embedding-004"
        });

        var options = new DbContextOptionsBuilder<OmniPrepDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=54329;Database=fake;Username=u;Password=p;Timeout=1;CommandTimeout=1")
            .Options;
        using var db = new OmniPrepDbContext(options);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var (isHealthy, statusCode, details) = await HealthEndpoints.CheckReadinessAsync(db, geminiOptionsMissing, cts.Token);

        Assert.False(isHealthy);
        Assert.Equal(503, statusCode);

        dynamic json = details;
        Assert.False((bool)json.Gemini.Configured);
    }
}
