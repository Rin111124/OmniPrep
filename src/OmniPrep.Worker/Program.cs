using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OmniPrep.Domain.Entities;
using OmniPrep.Domain.Enums;
using OmniPrep.Domain.Interfaces;
using OmniPrep.Infrastructure;
using OmniPrep.Infrastructure.Data;

// ============================================================
// OmniPrep.Worker - Automated Ingestion & Scraping Pipeline
// Triển khai luồng tự động 5 trạm:
// Trạm 1: ScraperService -> Quét web học liệu
// Trạm 2: FileHashFilter -> SHA-256 kiểm tra trùng lặp file
// Trạm 3: GeminiParserService -> Gemini 2.0 bóc tách JSON
// Trạm 4: DeduplicationService -> 2 tầng (SHA-256 + pgvector Cosine < 0.08)
// Trạm 5: Database & Mutation -> Lưu câu gốc + Sinh 2-3 biến thể
// ============================================================

var isRunOnce = args.Contains("--run-once") || Environment.GetEnvironmentVariable("RUN_ONCE") == "true";

var builder = Host.CreateApplicationBuilder(args);

// Đăng ký Infrastructure services
builder.Services.AddInfrastructure(builder.Configuration);

if (isRunOnce)
{
    // Chế độ chạy một lần (cho GitHub Actions Cron 02:00 AM)
    builder.Services.AddHostedService<OneTimePipelineWorker>();
}
else
{
    // Chế độ chạy nền liên tục (Background daemon)
    builder.Services.AddHostedService<IngestionWorker>();
}

var host = builder.Build();
await host.RunAsync();

// ============================================================
// RUN-ONCE WORKER (Dành cho GitHub Actions 02:00 AM)
// ============================================================
public sealed class OneTimePipelineWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<OneTimePipelineWorker> _logger;

    public OneTimePipelineWorker(
        IServiceProvider services,
        IHostApplicationLifetime lifetime,
        ILogger<OneTimePipelineWorker> logger)
    {
        _services = services;
        _lifetime = lifetime;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("=================================================");
        _logger.LogInformation("🚀 BẮT ĐẦU PIPELINE TỰ ĐỘNG HÓA 5 TRẠM (02:00 AM)");
        _logger.LogInformation("=================================================");

        try
        {
            using var scope = _services.CreateScope();
            var scraper = scope.ServiceProvider.GetRequiredService<IScraperService>();
            var paperRepo = scope.ServiceProvider.GetRequiredService<IExamPaperRepository>();
            var subjectRepo = scope.ServiceProvider.GetRequiredService<ISubjectRepository>();
            var ingestionService = scope.ServiceProvider.GetRequiredService<IIngestionService>();
            var db = scope.ServiceProvider.GetRequiredService<OmniPrepDbContext>();

            // Lặp qua các môn học cốt lõi (Địa lý & GDKT&PL)
            var subjects = new[] { SubjectCode.GEO, SubjectCode.GDKTPL };

            foreach (var code in subjects)
            {
                var subject = await subjectRepo.GetByCodeAsync(code, stoppingToken);
                if (subject is null) continue;

                _logger.LogInformation("\n--- Đang xử lý môn học: {Subject} ---", code);

                // TRẠM 1: Quét web học liệu thu thập đề mới
                var scrapedDocs = await scraper.ScrapeLatestDocumentsAsync(code, maxFiles: 3, stoppingToken);

                foreach (var doc in scrapedDocs)
                {
                    _logger.LogInformation("Trạm 1: Phát hiện đề: {Title} ({Source})", doc.Title, doc.SourceName);

                    // TRẠM 2: FileHashFilter (SHA-256)
                    var rawText = doc.TextContent ?? (doc.ContentBytes != null ? Encoding.UTF8.GetString(doc.ContentBytes) : null);
                    if (string.IsNullOrWhiteSpace(rawText))
                    {
                        _logger.LogWarning("Bỏ qua file trống: {Title}", doc.Title);
                        continue;
                    }

                    var fileHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawText))).ToLowerInvariant();
                    var existingPaper = await paperRepo.GetByFileHashAsync(fileHash, stoppingToken);

                    if (existingPaper is not null)
                    {
                        _logger.LogInformation("Trạm 2 [FileHashFilter]: Đề đã được xử lý (Hash={Hash}). XÓA & BỎ QUA.", fileHash[..8]);
                        continue;
                    }

                    // Lưu đề thi vào hàng đợi
                    var paper = new ExamPaper
                    {
                        Id = Guid.NewGuid(),
                        SourceId = await WorkerHelpers.GetOrCreateSourceIdAsync(db, doc.SourceName, stoppingToken),
                        SubjectId = subject.Id,
                        Title = doc.Title,
                        ExamYear = doc.ExamYear,
                        FileHash = fileHash,
                        RawContent = rawText,
                        Status = IngestionStatus.Processing,
                        SubmittedAt = DateTimeOffset.UtcNow
                    };
                    await paperRepo.AddAsync(paper, stoppingToken);
                    await paperRepo.SaveChangesAsync(stoppingToken);

                    // TRẠM 3, 4, 5: Chạy qua IngestionService
                    // (Trạm 3: Gemini Structured Output -> Trạm 4: Deduplication 2 tầng -> Trạm 5: Lưu DB & Mutation)
                    _logger.LogInformation("Trạm 3, 4, 5: Đang chạy pipeline bóc tách, dedup 2 tầng và sinh biến thể...");
                    var result = await ingestionService.IngestExamPaperAsync(
                        paper.Id, rawText, code, doc.SourceName, doc.ExamYear, stoppingToken);

                    paper.Status = result.TotalFailed == result.TotalExtracted && result.TotalExtracted > 0
                        ? IngestionStatus.Failed
                        : IngestionStatus.Completed;
                    await paperRepo.SaveChangesAsync(stoppingToken);

                    _logger.LogInformation(
                        "✅ Kết quả đề '{Title}': Trích xuất={Total}, Mới={New}, Gộp={Merged}, Lỗi={Failed}",
                        doc.Title, result.TotalExtracted, result.TotalNew, result.TotalMerged, result.TotalFailed);
                }
            }

            _logger.LogInformation("\n=================================================");
            _logger.LogInformation("🎉 HOÀN THÀNH PIPELINE TỰ ĐỘNG HÓA 5 TRẠM THÀNH CÔNG");
            _logger.LogInformation("=================================================");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi nghiêm trọng trong quá trình chạy Pipeline");
        }
        finally
        {
            _lifetime.StopApplication();
        }
    }
}

// ============================================================
// CONTINUOUS INGESTION WORKER (Dành cho Server Daemon)
// ============================================================
public sealed class IngestionWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<IngestionWorker> _logger;
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LeaseHeartbeatInterval = TimeSpan.FromSeconds(30);

    public IngestionWorker(IServiceProvider services, ILogger<IngestionWorker> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OmniPrep ingestion worker started");
        while (!stoppingToken.IsCancellationRequested)
        {
            var shouldWaitForPoll = true;
            try
            {
                using var scope = _services.CreateScope();
                var paperRepo = scope.ServiceProvider.GetRequiredService<IExamPaperRepository>();
                var ingestionService = scope.ServiceProvider.GetRequiredService<IIngestionService>();
                // Claim one at a time so queued-in-memory papers do not consume their leases.
                var papers = await paperRepo.ClaimPendingPapersAsync(batchSize: 1, stoppingToken);
                if (papers.Count > 0) shouldWaitForPoll = false;

                foreach (var paper in papers)
                {
                    var leaseId = paper.ProcessingLeaseId ?? throw new InvalidOperationException("Claimed paper has no lease identifier.");
                    using var processingCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    var heartbeatTask = MaintainProcessingLeaseAsync(paper.Id, leaseId, processingCts);
                    try
                    {
                        var result = await ingestionService.IngestExamPaperAsync(
                            paper.Id,
                            paper.RawContent!,
                            paper.Subject.Code,
                            paper.Source.Name,
                            paper.ExamYear,
                            processingCts.Token);
                        paper.TotalExtracted = result.TotalExtracted;
                        paper.TotalNew = result.TotalNew;
                        paper.TotalMerged = result.TotalMerged;
                        paper.ErrorMessage = result.Errors.Count > 0 ? string.Join("\n", result.Errors) : null;
                        paper.Status = result.TotalExtracted == 0 || result.TotalFailed == result.TotalExtracted
                            ? IngestionStatus.Failed
                            : IngestionStatus.Completed;
                        paper.ProcessedAt = DateTimeOffset.UtcNow;
                        paper.ProcessingStartedAt = null;
                        paper.ProcessingLeaseId = null;
                        if (!await paperRepo.UpdateClaimedPaperAsync(paper, leaseId, stoppingToken))
                            _logger.LogWarning("Paper {PaperId} completed after its lease was lost; result was not committed", paper.Id);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        shouldWaitForPoll = true;
                        _logger.LogError(ex, "Failed to process exam paper {PaperId}", paper.Id);
                        var willRetry = paper.ProcessingAttempts < 3;
                        paper.Status = willRetry ? IngestionStatus.Pending : IngestionStatus.Failed;
                        paper.ErrorMessage = $"Processing attempt {paper.ProcessingAttempts} failed ({ex.GetType().Name}).";
                        paper.ProcessedAt = willRetry ? null : DateTimeOffset.UtcNow;
                        paper.ProcessingStartedAt = null;
                        paper.ProcessingLeaseId = null;
                        if (!await paperRepo.UpdateClaimedPaperAsync(paper, leaseId, stoppingToken))
                            _logger.LogWarning("Paper {PaperId} failed after its lease was lost; retry state was not changed", paper.Id);
                    }
                    finally
                    {
                        processingCts.Cancel();
                        try { await heartbeatTask; }
                        catch (OperationCanceledException) when (processingCts.IsCancellationRequested) { }
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ingestion worker polling loop failed");
            }

            if (shouldWaitForPoll)
                await Task.Delay(PollingInterval, stoppingToken);
        }

        _logger.LogInformation("OmniPrep ingestion worker stopped");
    }

    private async Task MaintainProcessingLeaseAsync(Guid paperId, Guid leaseId, CancellationTokenSource processingCts)
    {
        var ct = processingCts.Token;
        while (true)
        {
            await Task.Delay(LeaseHeartbeatInterval, ct);
            try
            {
                using var scope = _services.CreateScope();
                var paperRepo = scope.ServiceProvider.GetRequiredService<IExamPaperRepository>();
                if (!await paperRepo.RefreshProcessingLeaseAsync(paperId, leaseId, ct))
                {
                    _logger.LogWarning("Ingestion lease for paper {PaperId} is no longer owned by this worker", paperId);
                    processingCts.Cancel();
                    return;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not refresh ingestion lease for paper {PaperId}", paperId);
            }
        }
    }
}
internal static class WorkerHelpers
{
    public static async Task<Guid> GetOrCreateSourceIdAsync(
        OmniPrepDbContext db,
        string sourceName,
        CancellationToken ct)
    {
        var source = await db.ExamSources.FirstOrDefaultAsync(s => s.Name == sourceName, ct);
        if (source is not null) return source.Id;

        source = new ExamSource
        {
            Id = Guid.NewGuid(),
            Name = sourceName,
            SourceType = "scraped",
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.ExamSources.Add(source);
        await db.SaveChangesAsync(ct);
        return source.Id;
    }
}
