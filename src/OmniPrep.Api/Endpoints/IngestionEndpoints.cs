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
            if (await paperRepo.GetByFileHashAsync(fileHash, ct) is not null)
                return Results.Conflict("Đề này đã được nạp trước đó.");

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

            // Store text durably for the background worker.
            var paper = new ExamPaper
            {
                Id = Guid.NewGuid(),
                SourceId = source.Id,
                SubjectId = subject.Id,
                Title = title,
                ExamYear = examYear,
                FileHash = fileHash,
                RawContent = contentToProcess,
                Status = IngestionStatus.Pending,
                SubmittedAt = DateTimeOffset.UtcNow
            };

            await paperRepo.AddAsync(paper, ct);
            await paperRepo.SaveChangesAsync(ct);

            return Results.Accepted(value: new
            {
                PaperId = paper.Id,
                Status = paper.Status.ToString(),
                Message = "Đề đã vào hàng đợi xử lý."
            });
        }).WithName("IngestExamPaper").DisableAntiforgery();

        return group;
    }
}
