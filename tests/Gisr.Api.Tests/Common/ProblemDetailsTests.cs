using System.Net;
using System.Net.Http.Json;
using Gisr.Api.Common;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Gisr.Api.Tests.Common;

public class ProblemDetailsTests
{
    private static async Task<IHost> StartAsync()
    {
        return await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddGisrProblemDetails();
                    services.AddAuthentication(TestAuthHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
                    services.AddAuthorizationBuilder()
                        .AddPolicy("NeedsAdmin", p => p.RequireRole("Admin"));
                })
                .Configure(app =>
                {
                    app.UseGisrProblemDetails();
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/boom", (Func<IResult>)(() => throw new InvalidOperationException("secret record content")));
                        endpoints.MapGet("/problem", (Func<IResult>)(() => throw new ProblemException(StatusCodes.Status409Conflict, "Conflict", "Version mismatch")));
                        endpoints.MapGet("/secure", () => Results.Ok()).RequireAuthorization();
                        endpoints.MapGet("/admin", () => Results.Ok()).RequireAuthorization("NeedsAdmin");
                    });
                }))
            .StartAsync();
    }

    [Fact]
    public async Task UnhandledException_Returns500ProblemDetails_WithoutLeakingContent()
    {
        using var host = await StartAsync();

        var response = await host.GetTestClient().GetAsync("/boom");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("secret record content", body, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
        Assert.DoesNotContain("at Gisr", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProblemException_MapsToItsStatusAndDetail()
    {
        using var host = await StartAsync();

        var response = await host.GetTestClient().GetAsync("/problem");
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Conflict", problem?.Title);
        Assert.Equal("Version mismatch", problem?.Detail);
    }

    [Fact]
    public async Task Unauthenticated_Returns401ProblemDetails()
    {
        using var host = await StartAsync();

        var response = await host.GetTestClient().GetAsync("/secure");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Forbidden_Returns403ProblemDetails()
    {
        using var host = await StartAsync();
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.OidHeader, Guid.NewGuid().ToString());

        var response = await client.GetAsync("/admin");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
