using System.Reflection;
using FluentValidation;
using Gisr.Shared.Records;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Gisr.Api.Tests.Common;

// The validation filter skips any argument without a validator, so a new request type must not be able to slip in unvalidated.
[Collection("api")]
public class ValidationCoverageTests(PostgresFixture postgres)
{
    private static IEnumerable<Type> InputTypes(RouteEndpoint endpoint)
    {
        foreach (var accepts in endpoint.Metadata.OfType<IAcceptsMetadata>())
        {
            if (accepts.RequestType is { } body && !body.Namespace!.StartsWith("Microsoft.AspNetCore.Http", StringComparison.Ordinal))
            {
                yield return body;
            }
        }

        var method = endpoint.Metadata.GetMetadata<MethodInfo>();
        foreach (var parameter in method?.GetParameters() ?? [])
        {
            if (parameter.GetCustomAttribute<AsParametersAttribute>() is not null)
            {
                yield return parameter.ParameterType;
            }
        }
    }

    [Fact]
    public async Task EveryRequestBodyAndQueryTypeOfEveryApiEndpoint_HasAValidator()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/", StringComparison.Ordinal) == true)
            .ToList();

        var inputTypes = endpoints.SelectMany(InputTypes).Distinct().ToList();

        Assert.Contains(typeof(CreateRecordRequest), inputTypes);
        Assert.Contains(typeof(UpdateRecordRequest), inputTypes);
        Assert.Contains(inputTypes, t => t.Name == "RecordListQuery");
        var unvalidated = inputTypes
            .Where(t => factory.Services.GetService(typeof(IValidator<>).MakeGenericType(t)) is null)
            .Select(t => t.FullName)
            .Order()
            .ToList();
        Assert.True(unvalidated.Count == 0, "No validator is registered for: " + string.Join(", ", unvalidated));
    }
}
