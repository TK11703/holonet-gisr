using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentValidation;
using Gisr.Api.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Gisr.Api.Tests.Common;

public class ValidationEndpointFilterTests
{
    public sealed record Body(string Name);

    public sealed class BodyValidator : AbstractValidator<Body>
    {
        public BodyValidator() => RuleFor(b => b.Name).NotEmpty().MaximumLength(5);
    }

    public sealed record PageQuery(int Page);

    public sealed class PageQueryValidator : AbstractValidator<PageQuery>
    {
        public PageQueryValidator() => RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
    }

    private static int _handlerCalls;

    private static async Task<IHost> StartAsync()
    {
        return await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddProblemDetails();
                    services.AddScoped<IValidator<Body>, BodyValidator>();
                    services.AddScoped<IValidator<PageQuery>, PageQueryValidator>();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        var group = endpoints.MapGroup("/test").AddEndpointFilter<ValidationEndpointFilter>();
                        group.MapPost("/echo", (Body body) =>
                        {
                            Interlocked.Increment(ref _handlerCalls);
                            return Results.Ok(body);
                        });
                        group.MapGet("/page", ([AsParameters] PageQuery query) =>
                        {
                            Interlocked.Increment(ref _handlerCalls);
                            return Results.Ok(query);
                        });
                    });
                }))
            .StartAsync();
    }

    [Fact]
    public async Task InvalidBody_Returns400ValidationProblem_AndSkipsHandler()
    {
        using var host = await StartAsync();
        var client = host.GetTestClient();
        var before = Volatile.Read(ref _handlerCalls);

        var response = await client.PostAsJsonAsync("/test/echo", new Body(""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains(problem.Errors.Keys, key => key.Equals("Name", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(before, Volatile.Read(ref _handlerCalls));
    }

    [Fact]
    public async Task ValidBody_PassesThroughToHandler()
    {
        using var host = await StartAsync();
        var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync("/test/echo", new Body("Han"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var echoed = await response.Content.ReadFromJsonAsync<Body>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("Han", echoed?.Name);
    }

    [Fact]
    public async Task InvalidQueryDto_Returns400()
    {
        using var host = await StartAsync();
        var client = host.GetTestClient();

        var response = await client.GetAsync("/test/page?page=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ValidQueryDto_PassesThrough()
    {
        using var host = await StartAsync();
        var client = host.GetTestClient();

        var response = await client.GetAsync("/test/page?page=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
