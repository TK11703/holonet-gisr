using System.Security.Claims;
using Gisr.Api.Common;

namespace Gisr.Api.Tests.Common;

public class AuthSetupTests
{
    private static ClaimsPrincipal Principal(params Claim[] claims) => new(new ClaimsIdentity(claims, "Test"));

    [Theory]
    [InlineData("scp", "access_as_user")]
    [InlineData("scp", "other access_as_user")]
    [InlineData("http://schemas.microsoft.com/identity/claims/scope", "access_as_user")]
    public void HasRequiredScope_True_ForRecognizedScopeClaims(string claimType, string value) =>
        Assert.True(AuthSetup.HasRequiredScope(Principal(new Claim(claimType, value))));

    [Theory]
    [InlineData("scp", "other")]
    [InlineData("scp", "access_as_user_extra")]
    [InlineData("roles", "access_as_user")]
    public void HasRequiredScope_False_Otherwise(string claimType, string value) =>
        Assert.False(AuthSetup.HasRequiredScope(Principal(new Claim(claimType, value))));

    [Fact]
    public void HasRequiredScope_False_WhenNoScopeClaim() =>
        Assert.False(AuthSetup.HasRequiredScope(Principal()));

    [Fact]
    public void CurrentUser_ReadsMappedClaimTypes()
    {
        var oid = Guid.NewGuid();
        var tid = Guid.NewGuid();

        var user = CurrentUser.From(Principal(
            new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", oid.ToString()),
            new Claim("http://schemas.microsoft.com/identity/claims/tenantid", tid.ToString()),
            new Claim(ClaimTypes.Role, "Officer"),
            new Claim(ClaimTypes.Name, "Mapped Name")));

        Assert.Equal(oid, user.ObjectId);
        Assert.Equal(tid, user.TenantId);
        Assert.True(user.IsOfficer);
        Assert.Equal("Mapped Name", user.DisplayName);
    }
}
