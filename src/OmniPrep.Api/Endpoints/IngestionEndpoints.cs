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
using OmniPrep.Infrastructure.Services;
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
        /// Bóc tách đề thi (hỗ trợ cả dán text trực tiếp hoặc tải file .txt/.pdf).
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
            IPdfDocumentExtractor pdfExtractor,
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
                var ext = Path.GetExtension(file.FileName);
                if (!string.Equals(ext, ".txt", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    return Results.BadRequest("Hệ thống hiện hỗ trợ file văn bản .txt hoặc tài liệu PDF (.pdf).");
                }
                if (file.Length > 10 * 1024 * 1024)
                {
                    return Results.BadRequest("Kích thước file tối đa là 10 MB.");
                }
                title = file.FileName;

                if (string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        contentToProcess = await pdfExtractor.ExtractTextAsync(file.OpenReadStream(), ct);
                        if (string.IsNullOrWhiteSpace(contentToProcess))
                        {
                            return Results.BadRequest("File PDF không chứa lớp văn bản (text layer) hoặc đã bị khóa/scan ảnh không có text.");
                        }
                    }
                    catch (Exception ex)
                    {
                        return Results.BadRequest($"Không thể trích xuất văn bản từ file PDF: {ex.Message}");
                    }
                }
                else
                {
                    using var reader = new StreamReader(file.OpenReadStream());
                    contentToProcess = await reader.ReadToEndAsync(ct);
                }
            }
            else if (!string.IsNullOrWhiteSpace(rawContent))
            {
                contentToProcess = rawContent;
            }
            else
            {
                return Results.BadRequest("Vui lòng tải lên file đề thi (.txt/.pdf) hoặc dán nội dung văn bản rawContent.");
            }

            var fileHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(contentToProcess))).ToLowerInvariant();
            if (contentToProcess.Length > 10 * 1024 * 1024)
                return Results.BadRequest("Nội dung đề tối đa là 10 MB.");

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
                    Part1Count = result.Part1Count,
                    Part2Count = result.Part2Count,
                    Part3Count = result.Part3Count,
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

        /// <summary>
        /// Danh sách tài liệu đề thi đã nạp phục vụ đối chiếu nguồn gốc (Mục 8 & 10 Lộ trình)
        /// </summary>
        ingestion.MapGet("/papers", async (
            ClaimsPrincipal user,
            OmniPrepDbContext db,
            CancellationToken ct) =>
        {
            if (!IsTeacherOrAdmin(user)) return Results.Forbid();
            var papers = await db.ExamPapers.AsNoTracking()
                .OrderByDescending(p => p.SubmittedAt)
                .Take(50)
                .Select(p => new
                {
                    p.Id,
                    p.Title,
                    Status = p.Status.ToString(),
                    p.SubmittedAt,
                    SubjectCode = p.Subject.Code.ToString(),
                    SubjectName = p.Subject.Name,
                    SourceName = p.Source != null ? p.Source.Name : string.Empty,
                    p.ExamYear,
                    RawLength = p.RawContent != null ? p.RawContent.Length : 0,
                    RawPreview = p.RawContent != null && p.RawContent.Length > 200 ? p.RawContent.Substring(0, 200) + "..." : (p.RawContent ?? string.Empty)
                })
                .ToListAsync(ct);
            return Results.Ok(papers);
        }).WithName("ListIngestedPapers");

        /// <summary>
        /// Chi tiết đề thi gốc để xem và đối chiếu từng câu hỏi (Source Provenance Inspection)
        /// </summary>
        ingestion.MapGet("/papers/{id:guid}", async (
            Guid id,
            ClaimsPrincipal user,
            OmniPrepDbContext db,
            CancellationToken ct) =>
        {
            if (!IsTeacherOrAdmin(user)) return Results.Forbid();
            var paper = await db.ExamPapers.AsNoTracking()
                .Include(p => p.Subject)
                .Include(p => p.Source)
                .FirstOrDefaultAsync(p => p.Id == id, ct);
            if (paper is null) return Results.NotFound();

            return Results.Ok(new
            {
                paper.Id,
                paper.Title,
                Status = paper.Status.ToString(),
                paper.SubmittedAt,
                SubjectCode = paper.Subject.Code.ToString(),
                SubjectName = paper.Subject.Name,
                SourceName = paper.Source?.Name ?? string.Empty,
                paper.ExamYear,
                paper.RawContent
            });
        }).WithName("GetIngestedPaperDetails");

        return group;
    }
}
