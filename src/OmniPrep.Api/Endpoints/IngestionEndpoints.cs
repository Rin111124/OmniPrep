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
        /// Bóc tách đề thi (hỗ trợ dán text trực tiếp, file .txt/.pdf hoặc tải ảnh chụp đề in ấn kèm AI tự động giải đề).
        /// </summary>
        ingestion.MapPost("/", async (
            HttpRequest request,
            ClaimsPrincipal user,
            OmniPrepDbContext db,
            IExamPaperRepository paperRepo,
            ISubjectRepository subjectRepo,
            IIngestionService ingestionService,
            IPdfDocumentExtractor pdfExtractor,
            CancellationToken ct) =>
        {
            if (!IsAdmin(user)) return Results.Forbid();

            if (!request.HasFormContentType)
                return Results.BadRequest("Yêu cầu phải có định dạng multipart/form-data.");

            var form = await request.ReadFormAsync(ct);
            var subjectCode = form["subjectCode"].ToString();
            var sourceName = form["sourceName"].ToString();
            var rawContent = form["rawContent"].ToString();
            var examYearStr = form["examYear"].ToString();
            short? examYear = short.TryParse(examYearStr, out var parsedYear) ? parsedYear : null;

            var autoSolveStr = form["autoSolve"].ToString();
            bool autoSolve = false; // Mặc định tắt auto-solve theo khuyến nghị P0
            if (!string.IsNullOrWhiteSpace(autoSolveStr) && bool.TryParse(autoSolveStr, out var parsedAutoSolve))
            {
                autoSolve = parsedAutoSolve;
            }

            if (!TryParseSubjectCode(subjectCode, out var code))
                return Results.BadRequest($"Mã môn học không hợp lệ: {subjectCode}");

            var subject = await subjectRepo.GetByCodeAsync(code, ct);
            if (subject is null) return Results.NotFound($"Môn học không tồn tại: {subjectCode}");

            var files = form.Files;
            var imageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".webp", ".bmp" };

            // Trường hợp 1: Tải lên các file ảnh (đề in chụp lại)
            var imageFiles = files.Where(f => imageExtensions.Contains(Path.GetExtension(f.FileName))).ToList();
            if (imageFiles.Count > 0)
            {
                var imageBytesList = new List<(byte[] Data, string MimeType)>();
                foreach (var imgFile in imageFiles)
                {
                    if (imgFile.Length == 0)
                        return Results.BadRequest($"File ảnh {imgFile.FileName} rỗng.");

                    if (imgFile.Length > 10 * 1024 * 1024)
                        return Results.BadRequest($"File {imgFile.FileName} vượt quá giới hạn 10 MB cho mỗi ảnh.");

                    using var ms = new MemoryStream();
                    await imgFile.CopyToAsync(ms, ct);
                    var bytes = ms.ToArray();

                    var ext = Path.GetExtension(imgFile.FileName).ToLowerInvariant();
                    if (!TryDetectImageMime(bytes, ext, out var mime))
                    {
                        return Results.BadRequest($"Định dạng file {imgFile.FileName} không hợp lệ hoặc bị hỏng.");
                    }

                    imageBytesList.Add((bytes, mime));
                }

                string imgTitle = imageFiles.Count == 1 
                    ? imageFiles[0].FileName 
                    : $"{imageFiles[0].FileName} (+{imageFiles.Count - 1} ảnh)";

                return await ExecuteImageIngestionPipelineAsync(
                    imageBytesList, imgTitle, code, sourceName, examYear, autoSolve,
                    subject, db, paperRepo, ingestionService, ct);
            }

            // Trường hợp 2: Tải lên file PDF hoặc TXT
            var firstDocFile = files.FirstOrDefault(f => 
                string.Equals(Path.GetExtension(f.FileName), ".pdf", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetExtension(f.FileName), ".txt", StringComparison.OrdinalIgnoreCase));

            if (firstDocFile is not null && firstDocFile.Length > 0)
            {
                var ext = Path.GetExtension(firstDocFile.FileName);
                if (firstDocFile.Length > 25 * 1024 * 1024)
                {
                    return Results.BadRequest("Kích thước file tối đa là 25 MB.");
                }

                string title = firstDocFile.FileName;
                string contentToProcess = string.Empty;

                if (string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        contentToProcess = await pdfExtractor.ExtractTextAsync(firstDocFile.OpenReadStream(), ct);
                    }
                    catch
                    {
                        contentToProcess = string.Empty;
                    }

                    if (string.IsNullOrWhiteSpace(contentToProcess))
                    {
                        // Scanned PDF (không có text layer): Chuyển sang Gemini Vision Multimodal OCR
                        using var ms = new MemoryStream();
                        await firstDocFile.CopyToAsync(ms, ct);
                        var pdfList = new List<(byte[] Data, string MimeType)> { (ms.ToArray(), "application/pdf") };
                        return await ExecuteImageIngestionPipelineAsync(
                            pdfList, title, code, sourceName, examYear, autoSolve,
                            subject, db, paperRepo, ingestionService, ct);
                    }
                }
                else
                {
                    using var reader = new StreamReader(firstDocFile.OpenReadStream());
                    contentToProcess = await reader.ReadToEndAsync(ct);
                }

                return await ExecuteIngestionPipelineAsync(
                    contentToProcess, title, null, code, sourceName, examYear,
                    subject, db, paperRepo, ingestionService, ct);
            }

            // Trường hợp 3: Dán nội dung text trực tiếp (rawContent)
            if (!string.IsNullOrWhiteSpace(rawContent))
            {
                return await ExecuteIngestionPipelineAsync(
                    rawContent, "Đề thi nạp qua văn bản", null, code, sourceName, examYear,
                    subject, db, paperRepo, ingestionService, ct);
            }

            return Results.BadRequest("Vui lòng tải lên file đề thi (.txt/.pdf/ảnh chụp đề in) hoặc dán nội dung văn bản rawContent.");
        }).WithName("IngestExamPaper").DisableAntiforgery();

        /// <summary>
        /// Danh mục kho đề thi trực tuyến mở có sẵn trên hệ thống (không cần nạp file thủ công)
        /// </summary>
        ingestion.MapGet("/online-catalog", async (
            string? subjectCode,
            ClaimsPrincipal user,
            OmniPrepDbContext db,
            CancellationToken ct) =>
        {
            if (!IsTeacherOrAdmin(user)) return Results.Forbid();

            var catalog = OnlineExamCatalog.GetCatalog();
            if (!string.IsNullOrWhiteSpace(subjectCode) && TryParseSubjectCode(subjectCode, out var code))
            {
                catalog = catalog.Where(c => c.SubjectCode == code).ToList();
            }

            var ingestedTitles = await db.ExamPapers.AsNoTracking()
                .Select(p => p.Title)
                .ToListAsync(ct);
            var ingestedTitleSet = new HashSet<string>(ingestedTitles, StringComparer.OrdinalIgnoreCase);

            var result = catalog.Select(c => new
            {
                c.Id,
                c.Title,
                SubjectCode = c.SubjectCode.ToString(),
                c.SourceName,
                c.ExamYear,
                c.DocumentType,
                c.TotalQuestionsEstimated,
                c.Description,
                c.RemoteUrl,
                IsAlreadyIngested = ingestedTitleSet.Contains(c.Title)
            });

            return Results.Ok(result);
        }).WithName("GetOnlineExamCatalog");

        /// <summary>
        /// Tự động tải file đề thi từ URL trực tuyến và bóc tách câu hỏi ngay trên máy chủ
        /// </summary>
        ingestion.MapPost("/from-url", async (
            IngestFromUrlRequest req,
            ClaimsPrincipal user,
            OmniPrepDbContext db,
            IExamPaperRepository paperRepo,
            ISubjectRepository subjectRepo,
            IIngestionService ingestionService,
            IPdfDocumentExtractor pdfExtractor,
            IHttpClientFactory httpClientFactory,
            CancellationToken ct) =>
        {
            if (!IsAdmin(user)) return Results.Forbid();
            if (!TryParseSubjectCode(req.SubjectCode, out var code))
                return Results.BadRequest($"Mã môn học không hợp lệ: {req.SubjectCode}");

            if (string.IsNullOrWhiteSpace(req.RemoteUrl) || !Uri.TryCreate(req.RemoteUrl, UriKind.Absolute, out var uri))
                return Results.BadRequest("Đường dẫn URL không hợp lệ.");

            if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
                return Results.BadRequest("Hệ thống chỉ chấp nhận giao thức web https/http.");

            if (uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest("Không được phép truy cập địa chỉ loopback hoặc mạng nội bộ.");

            var subject = await subjectRepo.GetByCodeAsync(code, ct);
            if (subject is null) return Results.NotFound($"Môn học không tồn tại: {req.SubjectCode}");

            string contentToProcess;
            string title = !string.IsNullOrWhiteSpace(req.Title) ? req.Title : Path.GetFileName(uri.LocalPath);
            if (string.IsNullOrWhiteSpace(title)) title = $"Đề thi trực tuyến từ {uri.Host}";

            try
            {
                using var client = httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(30);
                using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                var contentType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? "";
                var contentLength = response.Content.Headers.ContentLength;
                if (contentLength > 10 * 1024 * 1024)
                    return Results.BadRequest("File tải về vượt quá giới hạn 10 MB.");

                var isPdf = contentType.Contains("pdf") || uri.LocalPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

                if (isPdf)
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(ct);
                    contentToProcess = await pdfExtractor.ExtractTextAsync(stream, ct);
                }
                else
                {
                    contentToProcess = await response.Content.ReadAsStringAsync(ct);
                }

                if (string.IsNullOrWhiteSpace(contentToProcess))
                    return Results.BadRequest("Không thể đọc được văn bản đề thi từ đường dẫn đã cung cấp.");
            }
            catch (Exception ex)
            {
                return Results.BadRequest($"Lỗi khi tải tài liệu từ mạng: {ex.Message}");
            }

            return await ExecuteIngestionPipelineAsync(
                contentToProcess, title, req.RemoteUrl, code, req.SourceName, req.ExamYear,
                subject, db, paperRepo, ingestionService, ct);
        }).WithName("IngestFromUrl");

        /// <summary>
        /// Tự động tìm kiếm đề thi trực tuyến theo môn học, tự tải và bóc tách ngay
        /// </summary>
        ingestion.MapPost("/auto-fetch", async (
            AutoFetchOnlineRequest req,
            ClaimsPrincipal user,
            OmniPrepDbContext db,
            IExamPaperRepository paperRepo,
            ISubjectRepository subjectRepo,
            IIngestionService ingestionService,
            CancellationToken ct) =>
        {
            if (!IsAdmin(user)) return Results.Forbid();
            if (!TryParseSubjectCode(req.SubjectCode, out var code))
                return Results.BadRequest($"Mã môn học không hợp lệ: {req.SubjectCode}");

            var subject = await subjectRepo.GetByCodeAsync(code, ct);
            if (subject is null) return Results.NotFound($"Môn học không tồn tại: {req.SubjectCode}");

            var catalog = OnlineExamCatalog.GetCatalog().Where(c => c.SubjectCode == code).ToList();
            if (catalog.Count == 0)
                return Results.NotFound($"Chưa có đề thi trực tuyến trong kho cho môn {req.SubjectCode}.");

            OnlineExamItem? targetPaper;
            if (!string.IsNullOrWhiteSpace(req.CatalogId))
            {
                targetPaper = catalog.FirstOrDefault(c => c.Id == req.CatalogId);
            }
            else
            {
                var ingestedTitles = await db.ExamPapers.AsNoTracking()
                    .Select(p => p.Title)
                    .ToListAsync(ct);
                var ingestedTitleSet = new HashSet<string>(ingestedTitles, StringComparer.OrdinalIgnoreCase);
                targetPaper = catalog.FirstOrDefault(c => !ingestedTitleSet.Contains(c.Title)) ?? catalog[0];
            }

            if (targetPaper is null)
                return Results.NotFound("Không tìm thấy đề thi trực tuyến phù hợp.");

            string contentToProcess = targetPaper.GetContent();

            return await ExecuteIngestionPipelineAsync(
                contentToProcess, targetPaper.Title, targetPaper.RemoteUrl, code, targetPaper.SourceName, targetPaper.ExamYear,
                subject, db, paperRepo, ingestionService, ct);
        }).WithName("AutoFetchOnlineExam");

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

    private static async Task<IResult> ExecuteIngestionPipelineAsync(
        string contentToProcess,
        string title,
        string? fileUrl,
        SubjectCode code,
        string sourceName,
        short? examYear,
        Subject subject,
        OmniPrepDbContext db,
        IExamPaperRepository paperRepo,
        IIngestionService ingestionService,
        CancellationToken ct)
    {
        if (contentToProcess.Length > 10 * 1024 * 1024)
            return Results.BadRequest("Nội dung đề tối đa là 10 MB.");

        var fileHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(contentToProcess))).ToLowerInvariant();

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
                SourceType = "online_repository",
                WebsiteUrl = fileUrl,
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
            FileUrl = fileUrl,
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

        try
        {
            var result = await ingestionService.IngestExamPaperAsync(
                paper.Id, contentToProcess, code, source.Name, examYear, ct);

            bool isFailed = DetermineIsFailed(result.TotalExtracted, result.TotalFailed, result.TotalNew, result.TotalMerged);

            paper.Status = isFailed ? IngestionStatus.Failed : IngestionStatus.Completed;
            await paperRepo.SaveChangesAsync(ct);

            string message = isFailed
                ? (result.TotalExtracted == 0
                    ? "Không trích xuất được câu hỏi nào từ nội dung đề thi. Vui lòng kiểm tra lại chất lượng nội dung."
                    : $"Bóc tách không thành công: tất cả {result.TotalFailed} câu hỏi đều không vượt qua bước kiểm định chất lượng.")
                : $"Bóc tách hoàn tất: {result.TotalNew} câu hỏi mới được nạp, {result.TotalMerged} câu trùng lặp được gộp, {result.TotalFailed} câu lỗi.";

            var responseData = new
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
                Message = message
            };

            return isFailed && result.TotalExtracted == 0
                ? Results.BadRequest(responseData)
                : Results.Ok(responseData);
        }
        catch (Exception ex)
        {
            paper.Status = IngestionStatus.Failed;
            try { await paperRepo.SaveChangesAsync(ct); } catch { /* ignore secondary db fail */ }
            return Results.Json(new
            {
                title = "Bóc tách đề thi thất bại",
                message = $"Lỗi trong quá trình bóc tách đề thi: {ex.Message}",
                status = 500
            }, statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> ExecuteImageIngestionPipelineAsync(
        IReadOnlyList<(byte[] Data, string MimeType)> images,
        string title,
        SubjectCode code,
        string sourceName,
        short? examYear,
        bool autoSolve,
        Subject subject,
        OmniPrepDbContext db,
        IExamPaperRepository paperRepo,
        IIngestionService ingestionService,
        CancellationToken ct)
    {
        long totalBytes = images.Sum(img => (long)img.Data.Length);
        if (totalBytes > 25 * 1024 * 1024)
            return Results.BadRequest("Tổng dung lượng các file ảnh/tài liệu tối đa là 25 MB.");

        var hashBuilder = new StringBuilder();
        foreach (var img in images)
        {
            hashBuilder.Append(Convert.ToHexString(SHA256.HashData(img.Data)));
        }
        var fileHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashBuilder.ToString()))).ToLowerInvariant();

        var existingPaper = await paperRepo.GetByFileHashAsync(fileHash, ct);
        if (existingPaper is not null && existingPaper.Status == IngestionStatus.Completed)
            return Results.Conflict("Đề thi hình ảnh này đã được bóc tách và nạp thành công trước đó.");

        var source = await db.ExamSources.FirstOrDefaultAsync(s => s.Name == sourceName, ct);
        if (source is null)
        {
            source = new ExamSource
            {
                Id = Guid.NewGuid(),
                Name = sourceName,
                SourceType = images.Any(i => i.MimeType == "application/pdf") ? "scanned_pdf" : "image_scan",
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
            FileUrl = null,
            FileHash = fileHash,
            RawContent = $"[Multimodal Vision OCR & AI Auto-Solve: {images.Count} ảnh/tài liệu - Tự động giải: {(autoSolve ? "Bật" : "Tắt")}]",
            SubmittedAt = DateTimeOffset.UtcNow
        };

        paper.Status = IngestionStatus.Processing;
        if (existingPaper is null)
        {
            await paperRepo.AddAsync(paper, ct);
        }
        await paperRepo.SaveChangesAsync(ct);

        try
        {
            var result = await ingestionService.IngestExamPaperFromImagesAsync(
                paper.Id, images, code, source.Name, examYear, autoSolve, ct);

            bool isFailed = DetermineIsFailed(result.TotalExtracted, result.TotalFailed, result.TotalNew, result.TotalMerged);

            paper.Status = isFailed ? IngestionStatus.Failed : IngestionStatus.Completed;
            await paperRepo.SaveChangesAsync(ct);

            string message = isFailed
                ? (result.TotalExtracted == 0
                    ? "Không trích xuất được câu hỏi nào từ ảnh đề thi. Vui lòng kiểm tra lại độ rõ nét hoặc thử OCR trên trình duyệt."
                    : $"Bóc tách ảnh không thành công: tất cả {result.TotalFailed} câu hỏi đều không vượt qua bước kiểm định chất lượng.")
                : $"Bóc tách & giải đề qua ảnh hoàn tất: {result.TotalNew} câu hỏi mới được nạp, {result.TotalMerged} câu trùng lặp được gộp, {result.TotalFailed} câu lỗi.";

            var responseData = new
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
                Message = message
            };

            return isFailed && result.TotalExtracted == 0
                ? Results.BadRequest(responseData)
                : Results.Ok(responseData);
        }
        catch (Exception ex)
        {
            paper.Status = IngestionStatus.Failed;
            try { await paperRepo.SaveChangesAsync(ct); } catch { /* ignore secondary db fail */ }
            return Results.Json(new
            {
                title = "Bóc tách & giải đề qua hình ảnh thất bại",
                message = $"Lỗi trong quá trình xử lý hình ảnh qua AI: {ex.Message}",
                status = 500
            }, statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    internal static bool DetermineIsFailed(int totalExtracted, int totalFailed, int totalNew, int totalMerged)
    {
        return totalExtracted == 0 ||
               (totalFailed == totalExtracted && totalExtracted > 0) ||
               (totalNew == 0 && totalMerged == 0);
    }

    internal static bool TryDetectImageMime(byte[] bytes, string ext, out string mime)
    {
        mime = "application/octet-stream";
        if (bytes == null || bytes.Length < 4) return false;

        // JPEG: FF D8 FF
        if (bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            mime = "image/jpeg";
            return true;
        }

        // PNG: 89 50 4E 47
        if (bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            mime = "image/png";
            return true;
        }

        // WebP: RIFF .... WEBP
        if (bytes.Length > 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
            bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
        {
            mime = "image/webp";
            return true;
        }

        // BMP: 42 4D ('BM')
        if (bytes.Length > 2 && bytes[0] == 0x42 && bytes[1] == 0x4D)
        {
            mime = "image/bmp";
            return true;
        }

        // PDF: %PDF (25 50 44 46)
        if (bytes.Length > 4 && bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46)
        {
            mime = "application/pdf";
            return true;
        }

        // Fallback for known extensions
        if (ext is ".jpg" or ".jpeg") { mime = "image/jpeg"; return true; }
        if (ext is ".png") { mime = "image/png"; return true; }
        if (ext is ".webp") { mime = "image/webp"; return true; }
        if (ext is ".bmp") { mime = "image/bmp"; return true; }

        return false;
    }

    public sealed record IngestFromUrlRequest(string SubjectCode, string SourceName, string RemoteUrl, string? Title = null, short? ExamYear = null);
    public sealed record AutoFetchOnlineRequest(string SubjectCode, string? CatalogId = null);
}
