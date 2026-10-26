using Gisr.Shared.Affiliations;

namespace Gisr.Api.Affiliations;

public static class AffiliationEndpoints
{
    public static IEndpointRouteBuilder MapAffiliationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/affiliations").WithTags("Affiliations");

        group.MapGet("/", () => TypedResults.Ok(AffiliationCatalog.All))
            .WithName("listAffiliations")
            .WithSummary("Affiliations and their engagement field definitions")
            .Produces<IReadOnlyList<AffiliationDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return endpoints;
    }
}
