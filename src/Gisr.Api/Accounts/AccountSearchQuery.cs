using FluentValidation;

namespace Gisr.Api.Accounts;

public sealed record AccountSearchQuery(string? Query, int? PageSize);

public sealed class AccountSearchQueryValidator : AbstractValidator<AccountSearchQuery>
{
    public const int MinQueryLength = 2;
    public const int MaxQueryLength = 100;

    public AccountSearchQueryValidator()
    {
        RuleFor(q => q.Query)
            .Must(query => query is not null && query.Trim().Length is >= MinQueryLength and <= MaxQueryLength)
            .WithMessage($"'Query' must be between {MinQueryLength} and {MaxQueryLength} characters.");

        RuleFor(q => q.PageSize).InclusiveBetween(1, 25);
    }
}
