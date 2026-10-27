using Gisr.Api.Common;
using Gisr.Shared.Common;
using Gisr.Shared.Records;
using Microsoft.AspNetCore.Mvc;

namespace Gisr.Api.Records;

public static class RecordEndpoints
{
    public static IServiceCollection AddRecordServices(this IServiceCollection services)
    {
        services.AddScoped<RecordService>();
        services.AddScoped<OverrideService>();
        services.AddHostedService<ViewGrantPurgeService>();
        return services;
    }

    public static IEndpointRouteBuilder MapRecordEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/records")
            .WithTags("Records")
            .AddEndpointFilter<ValidationEndpointFilter>();

        group.MapGet("/", async ([AsParameters] RecordListQuery query, RecordService records, CancellationToken cancellationToken) =>
                TypedResults.Ok(await records.ListAsync(query, cancellationToken)))
            .WithName("listRecords")
            .Produces<PagedResult<RecordSummaryDto>>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/affiliations", async ([AsParameters] RecordAffiliationOptionsQuery query, RecordService records, CancellationToken cancellationToken) =>
                TypedResults.Ok(await records.AffiliationOptionsAsync(query, cancellationToken)))
            .WithName("listRecordAffiliations")
            .WithSummary("Distinct primary and secondary affiliations on the records the caller can list")
            .Produces<RecordAffiliationOptionsDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/", async (CreateRecordRequest request, RecordService records, CancellationToken cancellationToken) =>
            {
                var created = await records.CreateAsync(request, cancellationToken);
                return TypedResults.Created($"/api/records/{created.Id}", created);
            })
            .WithName("createRecord")
            .WithSummary("Create a record for the signed-in user")
            .Produces<RecordDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{recordId:guid}", async (Guid recordId, RecordService records, CancellationToken cancellationToken) =>
                TypedResults.Ok(await records.GetAsync(recordId, cancellationToken)))
            .WithName("getRecord")
            .Produces<RecordDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{recordId:guid}", async (Guid recordId, UpdateRecordRequest request, RecordService records, CancellationToken cancellationToken) =>
                TypedResults.Ok(await records.UpdateAsync(recordId, request, cancellationToken)))
            .WithName("updateRecord")
            .WithSummary("Replaces fields, incidents, and awards; bumps version and writes a revision")
            .Produces<RecordDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{recordId:guid}", async (Guid recordId, RecordService records, CancellationToken cancellationToken) =>
            {
                await records.DeleteAsync(recordId, cancellationToken);
                return TypedResults.NoContent();
            })
            .WithName("deleteRecord")
            .WithSummary("Soft-deletes a record; revisions and the access log are kept")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{recordId:guid}/history", async (Guid recordId, [AsParameters] HistoryQuery query, RecordService records, CancellationToken cancellationToken) =>
                TypedResults.Ok(await records.HistoryAsync(recordId, query, cancellationToken)))
            .WithName("getRecordHistory")
            .Produces<PagedResult<RevisionDto>>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{recordId:guid}/override", async (Guid recordId, OverrideCodeRequest request, OverrideService overrides, CancellationToken cancellationToken) =>
                TypedResults.Ok(await overrides.SubmitAsync(recordId, request, cancellationToken)))
            .WithName("submitOverrideCode")
            .WithSummary("Subject unlocks their own sealed record for one viewing")
            .Produces<UnlockedRecordDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapDelete("/{recordId:guid}/override", async (Guid recordId, [FromHeader(Name = "X-View-Token")] string? viewToken, OverrideService overrides, CancellationToken cancellationToken) =>
            {
                await overrides.EndAsync(recordId, viewToken, cancellationToken);
                return TypedResults.NoContent();
            })
            .WithName("endOverrideViewing")
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/{recordId:guid}/override/status", async (Guid recordId, [FromHeader(Name = "X-View-Token")] string? viewToken, OverrideService overrides, CancellationToken cancellationToken) =>
                await overrides.IsValidAsync(recordId, viewToken, cancellationToken)
                    ? Results.NoContent()
                    : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not Found"))
            .WithName("getOverrideStatus")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
