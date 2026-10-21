using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using OmniPrep.Domain.Entities;
using OmniPrep.Domain.Enums;
using OmniPrep.Domain.Interfaces;
using OmniPrep.Infrastructure.Data;
using static OmniPrep.Api.Endpoints.EndpointHelpers;

namespace OmniPrep.Api.Endpoints;

// ============================================================
// INGESTION - Nạp đề thi (Admin only)
// ============================================================
public static class IngestionEndpoints
{
    public static RouteGroupBuilder MapIngestionApi(this RouteGroupBuilder group)
    {
        var ingestion = group.MapGroup("/admin/ingest").WithTags("Admin - Ingestion").RequireAuthorization();

        /// <summary>
        /// Bóc tách đề thi (hỗ trợ cả dán text/OCR trực tiếp hoặc upload file).
        /// </summary>
        ingestion.MapPost("/", async (
            IFormFile? file,
            [FromForm] string? rawContent,
            [FromForm] string subjectCode,
            [FromForm] string sourceName,
            [FromForm] short? examYear,
            ClaimsPrincipal user,
            OmniPrepDbContext db,
            IExamPaperRepository paperRepo,
            ISubjectRepository subjectRepo,
            IIngestionService ingestionService,
            CancellationToken ct) =>
        {
            if (!IsAdmin(user)) return Results.Forbid();
            if (!Enum.TryParse<SubjectCode>(subjectCode, ignoreCase: true, out var code))
                return Results.BadRequest($"Mã môn học không hợp lệ: {subjectCode}");

            var subject = await subjectRepo.GetByCodeAsync(code, ct);
            if (subject is null) return Results.NotFound($"Môn học không tồn tại: {subjectCode}");

            string contentToProcess = string.Empty;
            string title = "Đề thi nạp qua văn bản";

            if (file is not null && file.Length > 0)
            {
                if (!string.Equals(Path.GetExtension(file.FileName), ".txt", StringComparison.OrdinalIgnoreCase))
                    return Results.BadRequest("Hiện chỉ hỗ trợ file .txt hoặc dán nội dung văn bản.");
                if (file.Length > 2 * 1024 * 1024)
                    return Results.BadRequest("Kích thước file tối đa là 2 MB.");
                title = file.FileName;
                using var reader = new StreamReader(file.OpenReadStream());
                contentToProcess = await reader.ReadToEndAsync(ct);
            }
            else if (!string.IsNullOrWhiteSpace(rawContent))
            {
                contentToProcess = rawContent;
            }
            else
            {
                return Results.BadRequest("Vui lòng tải lên file đề thi hoặc dán nội dung văn bản rawContent.");
            }

            var fileHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(contentToProcess))).ToLowerInvariant();
            if (contentToProcess.Length > 2 * 1024 * 1024)
                return Results.BadRequest("Nội dung đề tối đa là 2 MB.");

            var existingPaper = await paperRepo.GetByFileHashAsync(fileHash, ct);
            if (existingPaper is not null && existingPaper.Status == IngestionStatus.Completed)
                return Results.Conflict("Đề này đã được bóc tách và nạp thành công trước đó.");

            var source = await db.ExamSources.FirstOrDefaultAsync(s => s.Name == sourceName, ct);
            if (source is null)
            {
                source = new ExamSource
                {
                    Id = Guid.NewGuid(),
                    Name = sourceName,
                    SourceType = "manual",
                    CreatedAt = DateTimeOffset.UtcNow
                };
                db.ExamSources.Add(source);
                await db.SaveChangesAsync(ct);
            }

            var paper = existingPaper ?? new ExamPaper
            {
                Id = Guid.NewGuid(),
                SourceId = source.Id,
                SubjectId = subject.Id,
                Title = title,
                ExamYear = examYear,
                FileHash = fileHash,
                RawContent = contentToProcess,
                SubmittedAt = DateTimeOffset.UtcNow
            };

            paper.Status = IngestionStatus.Processing;
            if (existingPaper is null)
            {
                await paperRepo.AddAsync(paper, ct);
            }
            await paperRepo.SaveChangesAsync(ct);

            // Bóc tách trực tiếp bằng Gemini AI và chạy chống trùng lặp 2 tầng
            try
            {
                var result = await ingestionService.IngestExamPaperAsync(
                    paper.Id, contentToProcess, code, source.Name, examYear, ct);

                paper.Status = result.TotalFailed == result.TotalExtracted && result.TotalExtracted > 0
                    ? IngestionStatus.Failed
                    : IngestionStatus.Completed;
                await paperRepo.SaveChangesAsync(ct);

                return Results.Ok(new
                {
                    PaperId = paper.Id,
                    Status = paper.Status.ToString(),
                    TotalExtracted = result.TotalExtracted,
                    TotalNew = result.TotalNew,
                    TotalMerged = result.TotalMerged,
                    TotalFailed = result.TotalFailed,
                    Errors = result.Errors,
                    Message = $"Bóc tách hoàn tất: {result.TotalNew} câu hỏi mới được nạp, {result.TotalMerged} câu trùng lặp được gộp, {result.TotalFailed} câu lỗi."
                });
            }
            catch (Exception ex)
            {
                paper.Status = IngestionStatus.Failed;
                await paperRepo.SaveChangesAsync(ct);
                return Results.Problem(
                    title: "Bóc tách đề thi thất bại",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        }).WithName("IngestExamPaper").DisableAntiforgery();

        return group;
    }
}
