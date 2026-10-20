using System.Security.Claims;
using System.Text.Json;
using OmniPrep.Domain.Enums;

namespace OmniPrep.Api.Endpoints;

// ============================================================
// HELPER: Lấy User ID & Role từ JWT Claims (Supabase format)
// ============================================================
internal static class EndpointHelpers
{
    internal static Guid GetUserId(ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? user.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }

    internal static UserRole GetUserRole(ClaimsPrincipal user)
    {
        var roleClaim = user.FindFirstValue("omni_role")
                     ?? user.FindFirstValue(ClaimTypes.Role)
                     ?? user.FindFirstValue("role");
        if (string.Equals(roleClaim, "admin", StringComparison.OrdinalIgnoreCase)) return UserRole.Admin;
        if (string.Equals(roleClaim, "teacher", StringComparison.OrdinalIgnoreCase)) return UserRole.Teacher;

        var appMetadata = user.FindFirst("app_metadata")?.Value;
        if (!string.IsNullOrWhiteSpace(appMetadata))
        {
            try
            {
                using var metadata = JsonDocument.Parse(appMetadata);
                if (metadata.RootElement.TryGetProperty("role", out var role))
                {
                    if (string.Equals(role.GetString(), "ADMIN", StringComparison.OrdinalIgnoreCase)) return UserRole.Admin;
                    if (string.Equals(role.GetString(), "TEACHER", StringComparison.OrdinalIgnoreCase)) return UserRole.Teacher;
                }
            }
            catch (JsonException) { }
        }
        return UserRole.Student;
    }

    internal static bool IsAdmin(ClaimsPrincipal user) => GetUserRole(user) == UserRole.Admin;
    internal static bool IsTeacherOrAdmin(ClaimsPrincipal user) => GetUserRole(user) is UserRole.Teacher or UserRole.Admin;
}
