using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OmniPrep.Api.Endpoints;
using OmniPrep.Infrastructure.Data;
using Xunit;

namespace OmniPrep.Tests;

/// <summary>
/// Bộ kiểm thử an ninh phân quyền kiểm duyệt câu hỏi (Question Moderation RBAC Tests):
/// 1. Tài khoản vai trò STUDENT bị chặn triệt để (403 Forbidden) khi gọi bất kỳ API duyệt đề nào.
/// 2. Tài khoản TEACHER và ADMIN được phép truy cập.
/// 3. Rejection note bắt buộc phải từ 1-1000 ký tự.
/// </summary>
public class QuestionModerationSecurityTests
{
    private readonly Guid _studentId = Guid.NewGuid();
    private readonly Guid _teacherId = Guid.NewGuid();
    private readonly Guid _adminId = Guid.NewGuid();
    private readonly Guid _questionId = Guid.NewGuid();

    private static ClaimsPrincipal CreatePrincipalWithRole(Guid userId, string roleName) =>
        new(new ClaimsIdentity([
            new Claim("sub", userId.ToString()),
            new Claim("omni_role", roleName)
        ], "TestAuth"));

    private static OmniPrepDbContext CreateFakeDbContext()
    {
        var options = new DbContextOptionsBuilder<OmniPrepDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=54329;Database=fake;Username=u;Password=p;Timeout=1;CommandTimeout=1")
            .Options;
        return new OmniPrepDbContext(options);
    }

    [Theory]
    [InlineData("student")]
    [InlineData("anonymous")]
    [InlineData("guest")]
    public async Task ListPendingQuestions_WhenCalledByStudentOrUnauthorized_ReturnsForbid(string role)
    {
        using var db = CreateFakeDbContext();
        var studentPrincipal = CreatePrincipalWithRole(_studentId, role);
        var httpContext = new DefaultHttpContext();

        var result = await QuestionModerationEndpoints.HandleListPendingQuestionsAsync(
            limit: 50, offset: 0, user: studentPrincipal, db: db, httpContext: httpContext, ct: CancellationToken.None);

        Assert.IsType<ForbidHttpResult>(result);
    }

    [Theory]
    [InlineData("student")]
    [InlineData("guest")]
    public async Task VerifyQuestion_WhenCalledByStudent_ReturnsForbid(string role)
    {
        using var db = CreateFakeDbContext();
        var studentPrincipal = CreatePrincipalWithRole(_studentId, role);

        var result = await QuestionModerationEndpoints.HandleVerifyQuestionAsync(
            _questionId, studentPrincipal, db, CancellationToken.None);

        Assert.IsType<ForbidHttpResult>(result);
    }

    [Theory]
    [InlineData("student")]
    [InlineData("guest")]
    public async Task RejectQuestion_WhenCalledByStudent_ReturnsForbid(string role)
    {
        using var db = CreateFakeDbContext();
        var studentPrincipal = CreatePrincipalWithRole(_studentId, role);
        var req = new QuestionModerationEndpoints.RejectQuestionRequest("Lý do sai");

        var result = await QuestionModerationEndpoints.HandleRejectQuestionAsync(
            _questionId, req, studentPrincipal, db, CancellationToken.None);

        Assert.IsType<ForbidHttpResult>(result);
    }

    [Fact]
    public async Task RejectQuestion_WhenReasonIsMissingOrEmpty_ReturnsBadRequest()
    {
        using var db = CreateFakeDbContext();
        var teacherPrincipal = CreatePrincipalWithRole(_teacherId, "teacher");

        // Request không có lý do từ chối (bắt buộc theo quy định kiểm duyệt)
        var req = new QuestionModerationEndpoints.RejectQuestionRequest("");

        var result = await QuestionModerationEndpoints.HandleRejectQuestionAsync(
            _questionId, req, teacherPrincipal, db, CancellationToken.None);

        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(400, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [Theory]
    [InlineData("student")]
    [InlineData("guest")]
    public async Task UpdatePendingQuestion_WhenCalledByStudent_ReturnsForbid(string role)
    {
        using var db = CreateFakeDbContext();
        var studentPrincipal = CreatePrincipalWithRole(_studentId, role);
        var req = new QuestionModerationEndpoints.UpdateQuestionRequest(
            Content: "Sửa nội dung",
            AttachedTable: null,
            Topic: "Địa lý",
            Difficulty: "Easy",
            Explanation: "Giải thích",
            AnswerData: null,
            DistractorAnalysis: null);

        var result = await QuestionModerationEndpoints.HandleUpdatePendingQuestionAsync(
            _questionId, req, studentPrincipal, db, CancellationToken.None);

        Assert.IsType<ForbidHttpResult>(result);
    }

    [Theory]
    [InlineData("student")]
    [InlineData("guest")]
    public async Task GetQuestionModerationStats_WhenCalledByStudent_ReturnsForbid(string role)
    {
        using var db = CreateFakeDbContext();
        var studentPrincipal = CreatePrincipalWithRole(_studentId, role);
        var httpContext = new DefaultHttpContext();

        var result = await QuestionModerationEndpoints.HandleGetQuestionModerationStatsAsync(
            studentPrincipal, db, httpContext, CancellationToken.None);

        Assert.IsType<ForbidHttpResult>(result);
    }

    [Theory]
    [InlineData("student")]
    [InlineData("guest")]
    public async Task VerifyAllQuestions_WhenCalledByStudent_ReturnsForbid(string role)
    {
        using var db = CreateFakeDbContext();
        var studentPrincipal = CreatePrincipalWithRole(_studentId, role);
        var req = new QuestionModerationEndpoints.VerifyAllQuestionsRequest(null, "GEO");

        var result = await QuestionModerationEndpoints.HandleVerifyAllQuestionsAsync(
            req, studentPrincipal, db, CancellationToken.None);

        Assert.IsType<ForbidHttpResult>(result);
    }

    [Theory]
    [InlineData("teacher")]
    [InlineData("admin")]
    public async Task ListPendingQuestions_WhenCalledByTeacherOrAdmin_DoesNotReturnForbid(string privilegedRole)
    {
        using var db = CreateFakeDbContext();
        var privilegedPrincipal = CreatePrincipalWithRole(_adminId, privilegedRole);
        var httpContext = new DefaultHttpContext
        {
            RequestServices = new TestServiceProvider()
        };

        // Should NOT return Forbid (it will either proceed or catch DB error and return 500, but NEVER 403 Forbid)
        var result = await QuestionModerationEndpoints.HandleListPendingQuestionsAsync(
            limit: 50, offset: 0, user: privilegedPrincipal, db: db, httpContext: httpContext, ct: CancellationToken.None);

        Assert.IsNotType<ForbidHttpResult>(result);
    }

    private sealed class TestServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(Microsoft.Extensions.Logging.ILoggerFactory))
                return new Microsoft.Extensions.Logging.LoggerFactory();
            return null;
        }
    }
}
