using System.Security.Claims;
using Gisr.Web.Auth;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;

namespace Gisr.Web.Tests.Auth;

public class EntraAccessTokenProviderTests
{
    private sealed class FixedState(ClaimsPrincipal user) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(user));
    }

    private sealed class OutsideCircuit : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            throw new InvalidOperationException("Do not call GetAuthenticationStateAsync outside of the DI scope for a Razor component.");
    }

    private static ClaimsPrincipal Signed(string name) => new(new ClaimsIdentity([new Claim(ClaimTypes.Name, name)], "Test"));

    private static IHttpContextAccessor Accessor(ClaimsPrincipal? user) =>
        new HttpContextAccessor { HttpContext = user is null ? null : new DefaultHttpContext { User = user } };

    [Fact]
    public async Task InsideACircuit_TheAuthenticationStateUserIsUsed()
    {
        var user = await EntraAccessTokenProvider.ResolveUserAsync(new FixedState(Signed("circuit")), Accessor(Signed("http")));

        Assert.Equal("circuit", user.Identity!.Name);
    }

    [Fact]
    public async Task OutsideACircuit_TheHttpRequestUserIsUsed()
    {
        var user = await EntraAccessTokenProvider.ResolveUserAsync(new OutsideCircuit(), Accessor(Signed("http")));

        Assert.Equal("http", user.Identity!.Name);
    }

    [Fact]
    public async Task OutsideACircuit_WithoutASignedInRequestUser_TheFailureIsNotHidden()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            EntraAccessTokenProvider.ResolveUserAsync(new OutsideCircuit(), Accessor(null)));
    }
}
