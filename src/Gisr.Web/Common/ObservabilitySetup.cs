using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Gisr.Web.Common;

public static class ObservabilitySetup
{
    public static IServiceCollection AddGisrObservability(this IServiceCollection services, IConfiguration configuration, string serviceName)
    {
        var otel = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options =>
                {
                    // Query strings can contain search text, which must never leave the process.
                    options.EnrichWithHttpRequest = (activity, _) => activity.SetTag("url.query", null);
                    options.EnrichWithHttpResponse = (activity, _) => activity.SetTag("url.query", null);
                })
                .AddHttpClientInstrumentation(options =>
                    options.EnrichWithHttpRequestMessage = (activity, request) =>
                        activity.SetTag("url.full", request.RequestUri?.GetLeftPart(UriPartial.Path))))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation());

        if (!string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            otel.UseOtlpExporter();
        }

        return services;
    }
}
