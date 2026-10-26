using FluentValidation;
using Gisr.Api.Accounts;
using Gisr.Api.Affiliations;
using Gisr.Api.Common;
using Gisr.Api.Records;
using Gisr.Database.Records;
using Gisr.Database.Search;
using Gisr.Shared.Common;
using Gisr.Shared.Search;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Gisr.Api.Search;

public sealed record SearchQuery(string? Q, string? Scope, string? Affiliation, int? Page, int? PageSize);

public sealed class SearchQueryValidator : AbstractValidator<SearchQuery>
{
    public static readonly IReadOnlyList<string> Scopes = ["mine", "all"];

    public SearchQueryValidator()
    {
        RuleFor(q => q.Q)
            .Must(q => q is not null && q.Trim().Length is >= 2 and <= 500)
            .WithMessage("'Q' must be 2 to 500 characters long.");

        RuleFor(q => q.Scope)
            .Must(scope => Scopes.Contains(scope!, StringComparer.Ordinal))
            .WithMessage("'Scope' must be one of: " + string.Join(", ", Scopes) + ".")
            .When(q => q.Scope is not null);

        RuleFor(q => q.Affiliation)
            .Must(code => AffiliationCatalog.Find(code) is not null)
            .WithMessage("'Affiliation' must be one of: " + string.Join(", ", AffiliationCatalog.All.Select(a => a.Code)) + ".")
            .When(q => q.Affiliation is not null);

        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 50);
    }
}

public static class SearchResultMapper
{
    public const int MaxSnippetLength = 500;

    // Sealed is revealed only to Officers; a subject can never match a locked sealed record in the first place.
    public static SearchResultDto ToDto(SearchHitRow row, bool isOfficer) =>
        new(
            row.RecordId,
            row.RecordTitle,
            row.Affiliation,
            row.SubjectDisplayName,
            row.IsSealed && isOfficer,
            row.SourceType,
            row.SourceType == "artifact" ? row.ArtifactId : null,
            row.SourceType == "artifact" ? row.ArtifactFileName : null,
            Truncate(row.Content),
            Math.Clamp(1 - row.Distance, 0, 1));

    private static string Truncate(string text) =>
        text.Length <= MaxSnippetLength ? text : text[..(MaxSnippetLength - 1)] + "\u2026";
}

public sealed class UnavailableEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Search embeddings are not configured.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

public sealed class SearchService(
    SearchRepository search,
    IEmbeddingGenerator<string, Embedding<float>> embeddings,
    IOptions<SearchOptions> options,
    AccountService accounts,
    ICurrentUser user,
    ILogger<SearchService> logger)
{
    private const int DefaultPageSize = 10;

    public async Task<PagedResult<SearchResultDto>> SearchAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        var account = await accounts.EnsureAccountAsync(cancellationToken);
        var caller = new RecordCaller(account.Id, user.IsOfficer);
        var scope = string.Equals(query.Scope, "all", StringComparison.Ordinal) ? RecordListScope.All : RecordListScope.Mine;
        if (scope == RecordListScope.All && RecordAccessPolicy.ListAll(caller) != AccessDecision.Allowed)
        {
            throw new ProblemException(StatusCodes.Status403Forbidden, "Forbidden", "You are not allowed to search every record.");
        }

        var vector = await EmbedQueryAsync(query.Q!.Trim(), cancellationToken);
        var page = query.Page ?? 1;
        var pageSize = query.PageSize ?? DefaultPageSize;
        var rows = await search.SearchAsync(caller, scope, vector, query.Affiliation, options.Value.MaxDistance, page, pageSize, cancellationToken);

        return new PagedResult<SearchResultDto>(page, pageSize, rows.TotalCount, [.. rows.Items.Select(row => SearchResultMapper.ToDto(row, caller.IsOfficer))]);
    }

    // The query text goes to the embedding service and nowhere else: it is never logged and never put in an error.
    private async Task<float[]> EmbedQueryAsync(string text, CancellationToken cancellationToken)
    {
        try
        {
            return (await EmbeddingHelper.EmbedAsync(embeddings, [text], cancellationToken))[0];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("The embedding service could not embed a search query: {ErrorType}", EmbeddingHelper.Describe(ex));
            throw new ProblemException(StatusCodes.Status503ServiceUnavailable, "Service Unavailable", "Search is temporarily unavailable. Try again shortly.");
        }
    }
}

public static class SearchEndpoints
{
    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGroup("/api/search")
            .WithTags("Search")
            .AddEndpointFilter<ValidationEndpointFilter>()
            .MapGet("/", async ([AsParameters] SearchQuery query, SearchService service, CancellationToken cancellationToken) =>
                TypedResults.Ok(await service.SearchAsync(query, cancellationToken)))
            .WithName("search")
            .WithSummary("Semantic search across record text and file content, access-scoped in SQL")
            .Produces<PagedResult<SearchResultDto>>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }
}
