using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gisr.Api.Tests.Common;

public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string OidHeader = "X-Test-Oid";
    public const string TidHeader = "X-Test-Tid";
    public const string RolesHeader = "X-Test-Roles";
    public const string NameHeader = "X-Test-Name";
    public const string NoScopeHeader = "X-Test-NoScope";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(OidHeader, out var oid))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim> { new("oid", oid.ToString()) };

        if (Request.Headers.TryGetValue(TidHeader, out var tid))
        {
            claims.Add(new Claim("tid", tid.ToString()));
        }

        if (Request.Headers.TryGetValue(NameHeader, out var name))
        {
            claims.Add(new Claim("name", name.ToString()));
        }

        if (Request.Headers.TryGetValue(RolesHeader, out var roles))
        {
            claims.AddRange(roles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries).Select(r => new Claim("roles", r)));
        }

        if (!Request.Headers.ContainsKey(NoScopeHeader))
        {
            claims.Add(new Claim("scp", "access_as_user"));
        }

        var identity = new ClaimsIdentity(claims, SchemeName, "name", "roles");
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
