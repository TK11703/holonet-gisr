using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Gisr.Api.Common;

public sealed class GisrExceptionHandler(IProblemDetailsService problemDetailsService, ILogger<GisrExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails details;
        if (exception is ValidationProblemException invalid)
        {
            details = new HttpValidationProblemDetails(new Dictionary<string, string[]> { [invalid.Field] = [invalid.Detail ?? invalid.Title] })
            {
                Status = invalid.Status,
                Title = invalid.Title,
            };
        }
        else if (exception is ProblemException problem)
        {
            details = new ProblemDetails { Status = problem.Status, Title = problem.Title, Detail = problem.Detail };
            if (problem.Extensions is not null)
            {
                foreach (var (key, value) in problem.Extensions)
                {
                    details.Extensions[key] = value;
                }
            }

            if (problem.Headers is not null)
            {
                foreach (var (name, value) in problem.Headers)
                {
                    httpContext.Response.Headers[name] = value;
                }
            }
        }
        else
        {
            logger.LogError("Unhandled exception of type {ExceptionType}", exception.GetType().FullName);
            details = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
            };
        }

        httpContext.Response.StatusCode = details.Status!.Value;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = details,
        });
    }
}

public static class ProblemDetailsSetup
{
    public static IServiceCollection AddGisrProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails();
        services.AddExceptionHandler<GisrExceptionHandler>();
        return services;
    }

    public static IApplicationBuilder UseGisrProblemDetails(this IApplicationBuilder app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        return app;
    }
}
