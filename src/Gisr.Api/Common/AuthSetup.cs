using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;

namespace Gisr.Api.Common;

public static class AuthSetup
{
    public const string RequiredScope = "access_as_user";

    private const string MappedScopeClaim = "http://schemas.microsoft.com/identity/claims/scope";

    public static IServiceCollection AddGisrAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMicrosoftIdentityWebApiAuthentication(configuration, "AzureAd")
            .EnableTokenAcquisitionToCallDownstreamApi()
            .AddInMemoryTokenCaches();
        services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options => options.MapInboundClaims = false);
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser>(sp =>
            CurrentUser.From(sp.GetRequiredService<IHttpContextAccessor>().HttpContext!.User));

        services.AddAuthorizationBuilder().SetFallbackPolicy(
            new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireAssertion(context => HasRequiredScope(context.User))
                .Build());

        return services;
    }

    public static bool HasRequiredScope(System.Security.Claims.ClaimsPrincipal user) =>
        user.Claims
            .Where(claim => claim.Type is "scp" or MappedScopeClaim)
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(RequiredScope, StringComparer.Ordinal);
}
