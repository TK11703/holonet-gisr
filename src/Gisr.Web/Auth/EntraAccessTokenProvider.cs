using System.Security.Claims;
using Gisr.Web.Common;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;

namespace Gisr.Web.Auth;

public sealed class EntraAccessTokenProvider(
    ITokenAcquisition tokenAcquisition,
    AuthenticationStateProvider authentication,
    IHttpContextAccessor httpContextAccessor,
    IOptions<ApiOptions> options) : IAccessTokenProvider
{
    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        // The user is passed explicitly because there is no HttpContext inside a Blazor circuit.
        var user = await ResolveUserAsync(authentication, httpContextAccessor);
        return await tokenAcquisition.GetAccessTokenForUserAsync([options.Value.Scope], user: user);
    }

    // Plain HTTP endpoints (the media proxy) run outside a circuit, where the authentication state provider refuses to answer.
    public static async Task<ClaimsPrincipal> ResolveUserAsync(AuthenticationStateProvider authentication, IHttpContextAccessor httpContextAccessor)
    {
        try
        {
            return (await authentication.GetAuthenticationStateAsync()).User;
        }
        catch (InvalidOperationException) when (httpContextAccessor.HttpContext?.User is { Identity.IsAuthenticated: true })
        {
            return httpContextAccessor.HttpContext.User;
        }
    }
}
