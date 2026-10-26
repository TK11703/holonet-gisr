using FluentValidation;
using Gisr.Api.Affiliations;

namespace Gisr.Api.Records;

public sealed record RecordListQuery(string? Scope, string? Affiliation, string? Sort, int? Page, int? PageSize, Guid? SubjectAccountId = null);

public sealed record HistoryQuery(int? Page, int? PageSize);

public sealed class RecordListQueryValidator : AbstractValidator<RecordListQuery>
{
    public static readonly IReadOnlyList<string> Scopes = ["mine", "all"];
    public static readonly IReadOnlyList<string> Sorts = ["updatedDesc", "updatedAsc", "titleAsc", "titleDesc"];

    public RecordListQueryValidator()
    {
        RuleFor(q => q.Scope)
            .Must(scope => Scopes.Contains(scope!, StringComparer.Ordinal))
            .WithMessage("'Scope' must be one of: " + string.Join(", ", Scopes) + ".")
            .When(q => q.Scope is not null);

        RuleFor(q => q.Affiliation)
            .Must(code => AffiliationCatalog.Find(code) is not null)
            .WithMessage("'Affiliation' must be one of: " + string.Join(", ", AffiliationCatalog.All.Select(a => a.Code)) + ".")
            .When(q => q.Affiliation is not null);

        RuleFor(q => q.Sort)
            .Must(sort => Sorts.Contains(sort!, StringComparer.Ordinal))
            .WithMessage("'Sort' must be one of: " + string.Join(", ", Sorts) + ".")
            .When(q => q.Sort is not null);

        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);

        RuleFor(q => q.SubjectAccountId)
            .Must((q, _) => string.Equals(q.Scope, "all", StringComparison.Ordinal))
            .WithMessage("'Subject Account Id' can only be used with scope=all.")
            .When(q => q.SubjectAccountId is not null);
    }
}

public sealed class HistoryQueryValidator : AbstractValidator<HistoryQuery>
{
    public HistoryQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);
    }
}
