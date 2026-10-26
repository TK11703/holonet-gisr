using System.Security.Claims;
using Gisr.Web.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;

namespace Gisr.Web.Tests.Auth;

public class SessionCacheValidationTests
{
    private static CookieValidatePrincipalContext Context(bool authenticated = true)
    {
        var identity = authenticated ? new ClaimsIdentity("test") : new ClaimsIdentity();
        var principal = new ClaimsPrincipal(identity);
        var scheme = new AuthenticationScheme(CookieAuthenticationDefaults.AuthenticationScheme, null, typeof(CookieAuthenticationHandler));
        return new CookieValidatePrincipalContext(
            new DefaultHttpContext(), scheme, new CookieAuthenticationOptions(), new AuthenticationTicket(principal, scheme.Name));
    }

    private static MicrosoftIdentityWebChallengeUserException Challenge() =>
        new(new MsalUiRequiredException("user_null", "No account was passed."), ["api://test/access_as_user"], null);

    [Fact]
    public async Task AccountInTokenCache_KeepsTheSession()
    {
        var context = Context();
        var calls = 0;

        await SessionCacheValidation.ValidateAsync(context, _ =>
        {
            calls++;
            return Task.CompletedTask;
        });

        Assert.Equal(1, calls);
        Assert.NotNull(context.Principal);
    }

    [Fact]
    public async Task AccountMissingFromTokenCache_RejectsTheSession_SoTheUserSignsInAgain()
    {
        var context = Context();

        await SessionCacheValidation.ValidateAsync(context, _ => throw Challenge());

        Assert.Null(context.Principal);
    }

    [Fact]
    public async Task UnauthenticatedRequests_AreNotChecked()
    {
        var context = Context(authenticated: false);
        var calls = 0;

        await SessionCacheValidation.ValidateAsync(context, _ =>
        {
            calls++;
            return Task.CompletedTask;
        });

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task OtherFailures_AreNotSwallowed()
    {
        var context = Context();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SessionCacheValidation.ValidateAsync(context, _ => throw new InvalidOperationException("boom")));
    }
}
