using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using OmniPrep.Domain.Enums;
using OmniPrep.Infrastructure.Data;

namespace OmniPrep.Api.Endpoints;

// ============================================================
// EXAM SOURCES - Danh mục các nguồn đề thi uy tín
// ============================================================
public static class ExamSourcesEndpoints
{
    public static RouteGroupBuilder MapExamSourcesApi(this RouteGroupBuilder group)
    {
        var sources = group.MapGroup("/exam-sources").WithTags("Exam Sources");

        /// <summary>Lấy danh sách nguồn đề thi (Sở GD, trường chuyên, kỳ thi ĐGNL)</summary>
        sources.MapGet("/", async (
            OmniPrepDbContext db,
            string? type,
            string? province,
            string? search,
            CancellationToken ct) =>
        {
            var query = db.ExamSources.AsNoTracking().Where(s => s.IsActive);

            if (!string.IsNullOrWhiteSpace(type))
                query = query.Where(s => s.SourceType == type);

            if (!string.IsNullOrWhiteSpace(province))
                query = query.Where(s => s.Province == province);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                query = query.Where(s => s.Name.ToLower().Contains(term));
            }

            var list = await query
                .OrderBy(s => s.SourceType)
                .ThenBy(s => s.Province)
                .ThenBy(s => s.Name)
                .Select(s => new
                {
                    s.Id,
                    s.Name,
                    s.Province,
                    s.SourceType,
                    s.WebsiteUrl,
                    s.IsActive
                })
                .ToListAsync(ct);

            return Results.Ok(list);
        }).WithName("GetExamSources").AllowAnonymous();

        /// <summary>
        /// Tự động quét và thống kê nguồn đề thực tế theo môn học
        /// </summary>
        sources.MapGet("/scan", async (
            OmniPrepDbContext db,
            string subjectCode,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(subjectCode))
                return Results.BadRequest("Vui lòng cung cấp mã môn học subjectCode.");

            SubjectCode? parsedSubject = null;
            if (Enum.TryParse<SubjectCode>(subjectCode, ignoreCase: true, out var sc))
            {
                parsedSubject = sc;
            }
            else
            {
                parsedSubject = subjectCode.Trim().ToUpperInvariant() switch
                {
                    "GEOGRAPHY" => SubjectCode.GEO,
                    "LAW_ECONOMICS" => SubjectCode.GDKTPL,
                    "MATHEMATICS" => SubjectCode.MATH,
                    "PHYSICS" => SubjectCode.PHYS,
                    "CHEMISTRY" => SubjectCode.CHEM,
                    "BIOLOGY" => SubjectCode.BIO,
                    "HISTORY" => SubjectCode.HIS,
                    "ENGLISH" => SubjectCode.ENG,
                    _ => null
                };
            }

            if (!parsedSubject.HasValue)
                return Results.BadRequest($"Mã môn học không hợp lệ: {subjectCode}");

            var subject = await db.Subjects.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Code == parsedSubject.Value, ct);

            if (subject is null)
                return Results.NotFound($"Không tìm thấy môn học: {subjectCode}");

            // 1. Quét toàn bộ câu hỏi đã duyệt và đang hoạt động của môn này
            var questions = await db.Questions.AsNoTracking()
                .Where(q => q.SubjectId == subject.Id && q.IsActive && q.IsVerified)
                .Select(q => new
                {
                    q.Id,
                    q.Format,
                    q.SourceTags
                })
                .ToListAsync(ct);

            // 2. Danh mục nguồn chuẩn từ bảng ExamSources
            var knownSources = await db.ExamSources.AsNoTracking()
                .Where(s => s.IsActive)
                .ToListAsync(ct);

            // 3. Phân tích và nhóm số lượng câu theo từng nguồn
            var sourceStats = new Dictionary<string, (int total, int part1, int part2, int part3)>(StringComparer.OrdinalIgnoreCase);

            foreach (var q in questions)
            {
                if (q.SourceTags is null || q.SourceTags.Count == 0) continue;

                foreach (var rawTag in q.SourceTags)
                {
                    var tag = rawTag.Trim();
                    if (string.IsNullOrWhiteSpace(tag)) continue;

                    if (!sourceStats.TryGetValue(tag, out var stat))
                    {
                        stat = (0, 0, 0, 0);
                    }

                    stat.total++;
                    if (q.Format == QuestionFormat.MultipleChoice) stat.part1++;
                    else if (q.Format == QuestionFormat.TrueFalse) stat.part2++;
                    else if (q.Format == QuestionFormat.ShortAnswer) stat.part3++;

                    sourceStats[tag] = stat;
                }
            }

            var matrix = subject.ExamMatrix;
            var reqPart1 = matrix?.Part1.Count ?? 0;
            var reqPart2 = matrix?.Part2.Count ?? 0;
            var reqPart3 = matrix?.Part3.Count ?? 0;
            var reqTotal = reqPart1 + reqPart2 + reqPart3;

            // 4. Kết nối metadata nguồn (loại nguồn, tỉnh thành)
            var scannedList = sourceStats.Select(kvp =>
            {
                var sourceName = kvp.Key;
                var stat = kvp.Value;

                var match = knownSources.FirstOrDefault(s =>
                    string.Equals(s.Name, sourceName, StringComparison.OrdinalIgnoreCase) ||
                    sourceName.Contains(s.Name, StringComparison.OrdinalIgnoreCase) ||
                    s.Name.Contains(sourceName, StringComparison.OrdinalIgnoreCase));

                var isFullMatrix = reqTotal > 0
                    && stat.part1 >= reqPart1
                    && stat.part2 >= reqPart2
                    && stat.part3 >= reqPart3;

                var coverageScore = reqTotal > 0
                    ? Math.Min(100, Math.Round((stat.total / (double)reqTotal) * 100, 1))
                    : 100;

                return new
                {
                    Name = sourceName,
                    Province = match?.Province,
                    SourceType = match?.SourceType ?? "so_gd",
                    TotalQuestions = stat.total,
                    Part1Count = stat.part1,
                    Part2Count = stat.part2,
                    Part3Count = stat.part3,
                    IsFullMatrix = isFullMatrix,
                    CoverageScore = coverageScore
                };
            })
            .OrderByDescending(s => s.IsFullMatrix)
            .ThenByDescending(s => s.TotalQuestions)
            .ThenBy(s => s.Name)
            .ToList();

            // 5. Chọn nguồn khuyến nghị tối ưu (recommendedSource)
            var topCandidate = scannedList.FirstOrDefault(s => s.IsFullMatrix)
                ?? scannedList.FirstOrDefault();

            var recommended = topCandidate?.Name ?? "RANDOM";

            return Results.Ok(new
            {
                SubjectCode = subject.Code.ToString(),
                SubjectName = subject.Name,
                TotalAvailableQuestions = questions.Count,
                RequiredMatrix = new
                {
                    Part1 = reqPart1,
                    Part2 = reqPart2,
                    Part3 = reqPart3,
                    Total = reqTotal
                },
                RecommendedSource = recommended,
                Sources = scannedList
            });
        }).WithName("ScanExamSourcesBySubject").AllowAnonymous();

        return group;
    }
}
