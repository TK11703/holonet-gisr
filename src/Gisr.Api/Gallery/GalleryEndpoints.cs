using FluentValidation;
using Gisr.Api.Accounts;
using Gisr.Api.Common;
using Gisr.Api.Records;
using Gisr.Database.Gallery;
using Gisr.Database.Records;
using Gisr.Shared.Common;
using Gisr.Shared.Gallery;

namespace Gisr.Api.Gallery;

public sealed record GalleryQuery(string? Scope, int? Page, int? PageSize);

public sealed class GalleryQueryValidator : AbstractValidator<GalleryQuery>
{
    public static readonly IReadOnlyList<string> Scopes = ["mine", "all"];

    public GalleryQueryValidator()
    {
        RuleFor(q => q.Scope)
            .Must(scope => Scopes.Contains(scope!, StringComparer.Ordinal))
            .WithMessage("'Scope' must be one of: " + string.Join(", ", Scopes) + ".")
            .When(q => q.Scope is not null);

        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 50);
    }
}

public static class GalleryMapper
{
    public const int MaxCovers = 4;

    public static GalleryGroupDto ToDto(GalleryGroupRow row) =>
        new(row.RecordId, row.Title, row.Affiliation, row.SubjectDisplayName, row.ImageCount, [.. row.CoverArtifactIds.Take(MaxCovers)]);
}

public sealed class GalleryService(GalleryRepository gallery, AccountService accounts, ICurrentUser user)
{
    private const int DefaultPageSize = 12;

    public async Task<PagedResult<GalleryGroupDto>> ListAsync(GalleryQuery query, CancellationToken cancellationToken)
    {
        var account = await accounts.EnsureAccountAsync(cancellationToken);
        var caller = new RecordCaller(account.Id, user.IsOfficer);
        var scope = string.Equals(query.Scope, "all", StringComparison.Ordinal) ? RecordListScope.All : RecordListScope.Mine;
        if (scope == RecordListScope.All && RecordAccessPolicy.ListAll(caller) != AccessDecision.Allowed)
        {
            throw new ProblemException(StatusCodes.Status403Forbidden, "Forbidden", "You are not allowed to view every record's gallery.");
        }

        var page = query.Page ?? 1;
        var pageSize = query.PageSize ?? DefaultPageSize;
        var rows = await gallery.ListGroupsAsync(caller, scope, page, pageSize, cancellationToken);
        return new PagedResult<GalleryGroupDto>(page, pageSize, rows.TotalCount, [.. rows.Items.Select(GalleryMapper.ToDto)]);
    }
}

public static class GalleryEndpoints
{
    public static IServiceCollection AddGalleryServices(this IServiceCollection services)
    {
        services.AddScoped<GalleryService>();
        return services;
    }

    public static IEndpointRouteBuilder MapGalleryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGroup("/api/gallery")
            .WithTags("Gallery")
            .AddEndpointFilter<ValidationEndpointFilter>()
            .MapGet("/", async ([AsParameters] GalleryQuery query, GalleryService service, CancellationToken cancellationToken) =>
                TypedResults.Ok(await service.ListAsync(query, cancellationToken)))
            .WithName("listGalleryGroups")
            .WithSummary("One group per record that has images; sealed records are never included")
            .Produces<PagedResult<GalleryGroupDto>>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return endpoints;
    }
}
