using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Identity.Web;

namespace Gisr.Web.Auth;

public static class SessionCacheValidation
{
    // The token cache is in memory, so a session cookie that outlives a restart has no cached account.
    public static IServiceCollection RejectSessionsMissingFromTokenCache(this IServiceCollection services, string scope) =>
        services.Configure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, options =>
        {
            var inner = options.Events.OnValidatePrincipal;
            options.Events.OnValidatePrincipal = async context =>
            {
                await inner(context);
                if (string.IsNullOrWhiteSpace(scope))
                {
                    return;
                }

                var tokens = context.HttpContext.RequestServices.GetRequiredService<ITokenAcquisition>();
                await ValidateAsync(context, principal => tokens.GetAccessTokenForUserAsync([scope], user: principal));
            };
        });

    public static async Task ValidateAsync(CookieValidatePrincipalContext context, Func<ClaimsPrincipal, Task> acquireToken)
    {
        if (context.Principal?.Identity?.IsAuthenticated != true)
        {
            return;
        }

        try
        {
            await acquireToken(context.Principal);
        }
        catch (MicrosoftIdentityWebChallengeUserException)
        {
            context.RejectPrincipal();
        }
    }
}
