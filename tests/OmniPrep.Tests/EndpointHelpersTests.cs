using System.Security.Claims;
using OmniPrep.Api.Endpoints;
using OmniPrep.Domain.Enums;
using Xunit;

namespace OmniPrep.Tests;

public class EndpointHelpersTests
{
    [Fact]
    public void GetUserId_FromSubClaim_ReturnsGuid()
    {
        var expectedId = Guid.NewGuid();
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("sub", expectedId.ToString())
        }, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var actualId = EndpointHelpers.GetUserId(principal);
        Assert.Equal(expectedId, actualId);
    }

    [Fact]
    public void GetUserId_InvalidOrMissing_ReturnsGuidEmpty()
    {
        var identity = new ClaimsIdentity();
        var principal = new ClaimsPrincipal(identity);

        var actualId = EndpointHelpers.GetUserId(principal);
        Assert.Equal(Guid.Empty, actualId);
    }

    [Theory]
    [InlineData("admin", UserRole.Admin)]
    [InlineData("teacher", UserRole.Teacher)]
    [InlineData("student", UserRole.Student)]
    public void GetUserRole_FromRoleClaim_ResolvesCorrectly(string roleStr, UserRole expectedRole)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, roleStr)
        }, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var actualRole = EndpointHelpers.GetUserRole(principal);
        Assert.Equal(expectedRole, actualRole);
    }

    [Theory]
    [InlineData("{\"role\":\"ADMIN\"}", UserRole.Admin)]
    [InlineData("{\"role\":\"TEACHER\"}", UserRole.Teacher)]
    [InlineData("{\"role\":\"STUDENT\"}", UserRole.Student)]
    [InlineData("{}", UserRole.Student)]
    [InlineData("invalid-json", UserRole.Student)]
    public void GetUserRole_FromAppMetadata_ResolvesCorrectly(string appMetadataJson, UserRole expectedRole)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("app_metadata", appMetadataJson)
        }, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var actualRole = EndpointHelpers.GetUserRole(principal);
        Assert.Equal(expectedRole, actualRole);
    }

    [Fact]
    public void IsTeacherOrAdmin_ReturnsTrueForTeacherAndAdminOnly()
    {
        var admin = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("omni_role", "admin") }));
        var teacher = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("omni_role", "teacher") }));
        var student = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("omni_role", "student") }));

        Assert.True(EndpointHelpers.IsTeacherOrAdmin(admin));
        Assert.True(EndpointHelpers.IsTeacherOrAdmin(teacher));
        Assert.False(EndpointHelpers.IsTeacherOrAdmin(student));
    }

    [Fact]
    public void GetUserRole_UserMetadataNeverGrantsPrivileges()
    {
        // P0: User metadata can be manipulated by users, backend MUST NOT use it for authorization
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("user_metadata", "{\"role\":\"ADMIN\"}")
        }, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var actualRole = EndpointHelpers.GetUserRole(principal);
        Assert.Equal(UserRole.Student, actualRole);
        Assert.False(EndpointHelpers.IsTeacherOrAdmin(principal));
    }
}
