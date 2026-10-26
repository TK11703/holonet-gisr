using Gisr.Api.Common;
using Gisr.Shared.Artifacts;
using Gisr.Shared.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Gisr.Api.Artifacts;

public static class ArtifactEndpoints
{
    private const string ViewTokenHeader = "X-View-Token";

    public static IServiceCollection AddArtifactServices(this IServiceCollection services)
    {
        services.AddScoped<ArtifactService>();
        return services;
    }

    public static IEndpointRouteBuilder MapArtifactEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api")
            .WithTags("Artifacts")
            .AddEndpointFilter<ValidationEndpointFilter>();

        group.MapGet("/records/{recordId:guid}/artifacts", async (
                Guid recordId,
                [FromHeader(Name = ViewTokenHeader)] string? viewToken,
                [AsParameters] ArtifactListQuery query,
                ArtifactService service,
                CancellationToken cancellationToken) =>
                TypedResults.Ok(await service.ListAsync(recordId, viewToken, query, cancellationToken)))
            .WithName("listArtifacts")
            .Produces<PagedResult<ArtifactDto>>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Bearer tokens, not cookies, authenticate this Api, so form posts carry no antiforgery token.
        group.MapPost("/records/{recordId:guid}/artifacts", async (
                Guid recordId,
                IFormFile? file,
                [FromForm] string? description,
                ArtifactService service,
                CancellationToken cancellationToken) =>
            {
                if (file is null)
                {
                    throw new ValidationProblemException("file", "A file is required.");
                }

                await using var content = file.OpenReadStream();
                var created = await service.UploadAsync(recordId, new ArtifactUploadContent(file.FileName, file.Length, content, description), cancellationToken);
                return TypedResults.Created($"/api/artifacts/{created.Id}", created);
            })
            .DisableAntiforgery()
            .Accepts<IFormFile>("multipart/form-data")
            .WithName("uploadArtifact")
            .Produces<ArtifactDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);

        group.MapPatch("/artifacts/{artifactId:guid}", async (Guid artifactId, UpdateArtifactRequest request, ArtifactService service, CancellationToken cancellationToken) =>
                TypedResults.Ok(await service.UpdateDescriptionAsync(artifactId, request, cancellationToken)))
            .WithName("updateArtifactDescription")
            .Produces<ArtifactDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/artifacts/{artifactId:guid}", async (Guid artifactId, ArtifactService service, CancellationToken cancellationToken) =>
            {
                await service.RemoveAsync(artifactId, cancellationToken);
                return TypedResults.NoContent();
            })
            .WithName("removeArtifact")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/artifacts/{artifactId:guid}/content", (
                Guid artifactId,
                [FromHeader(Name = ViewTokenHeader)] string? viewToken,
                ArtifactService service,
                HttpContext http,
                CancellationToken cancellationToken) => StreamAsync(service, http, artifactId, viewToken, thumbnail: false, cancellationToken))
            .WithName("getArtifactContent")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/artifacts/{artifactId:guid}/thumbnail", (
                Guid artifactId,
                [FromHeader(Name = ViewTokenHeader)] string? viewToken,
                ArtifactService service,
                HttpContext http,
                CancellationToken cancellationToken) => StreamAsync(service, http, artifactId, viewToken, thumbnail: true, cancellationToken))
            .WithName("getArtifactThumbnail")
            .Produces(StatusCodes.Status200OK, contentType: "image/jpeg")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> StreamAsync(
        ArtifactService service,
        HttpContext http,
        Guid artifactId,
        string? viewToken,
        bool thumbnail,
        CancellationToken cancellationToken)
    {
        var download = await service.OpenAsync(artifactId, viewToken, thumbnail, cancellationToken);
        var headers = http.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.CacheControl = "private, no-store";
        if (!download.IsImage)
        {
            headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileNameStar = download.FileName }.ToString();
        }

        return Results.Stream(download.Blob.Content, download.ContentType);
    }
}
