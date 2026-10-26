using System.Security.Claims;
using Gisr.Api.Common;
using Microsoft.AspNetCore.Http;

namespace Gisr.Api.Tests.Common;

public class CurrentUserTests
{
    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "Test"));

    private static readonly Guid Oid = Guid.NewGuid();
    private static readonly Guid Tid = Guid.NewGuid();

    [Fact]
    public void From_ReadsObjectIdAndTenantId()
    {
        var user = CurrentUser.From(Principal(new Claim("oid", Oid.ToString()), new Claim("tid", Tid.ToString())));

        Assert.Equal(Oid, user.ObjectId);
        Assert.Equal(Tid, user.TenantId);
    }

    [Fact]
    public void From_IsOfficer_OnlyWhenRolesContainOfficer()
    {
        var officer = CurrentUser.From(Principal(
            new Claim("oid", Oid.ToString()), new Claim("tid", Tid.ToString()),
            new Claim("roles", "Reader"), new Claim("roles", "Officer")));
        var regular = CurrentUser.From(Principal(
            new Claim("oid", Oid.ToString()), new Claim("tid", Tid.ToString()), new Claim("roles", "Reader")));
        var lowerCase = CurrentUser.From(Principal(
            new Claim("oid", Oid.ToString()), new Claim("tid", Tid.ToString()), new Claim("roles", "officer")));

        Assert.True(officer.IsOfficer);
        Assert.False(regular.IsOfficer);
        Assert.False(lowerCase.IsOfficer);
    }

    [Fact]
    public void From_ReadsDisplayNameAndEmail()
    {
        var user = CurrentUser.From(Principal(
            new Claim("oid", Oid.ToString()), new Claim("tid", Tid.ToString()),
            new Claim("name", "Leia Organa"), new Claim("preferred_username", "leia@rebellion.example")));

        Assert.Equal("Leia Organa", user.DisplayName);
        Assert.Equal("leia@rebellion.example", user.Email);
    }

    [Fact]
    public void From_Throws403_WhenObjectIdMissing()
    {
        var ex = Assert.Throws<ProblemException>(() => CurrentUser.From(Principal(new Claim("tid", Tid.ToString()))));

        Assert.Equal(StatusCodes.Status403Forbidden, ex.Status);
    }
}
