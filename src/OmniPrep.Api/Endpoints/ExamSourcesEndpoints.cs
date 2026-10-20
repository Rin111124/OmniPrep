using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
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

        return group;
    }
}
