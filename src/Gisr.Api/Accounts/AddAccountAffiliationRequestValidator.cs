using FluentValidation;
using Gisr.Shared.Accounts;

namespace Gisr.Api.Accounts;

public sealed class AddAccountAffiliationRequestValidator : AbstractValidator<AddAccountAffiliationRequest>
{
    public const int NameMaxLength = 100;

    public AddAccountAffiliationRequestValidator()
    {
        RuleFor(r => r.Primary).NotEmpty().WithMessage("Choose a primary affiliation.");
        RuleFor(r => r.Primary)
            .Must(name => AffiliationOptionCatalog.Contains(name.Trim()))
            .When(r => !string.IsNullOrWhiteSpace(r.Primary))
            .WithMessage("Choose a primary affiliation from the list.");
        RuleFor(r => r.Secondary)
            .Must(name => AffiliationOptionCatalog.Contains(name!.Trim()))
            .When(r => !string.IsNullOrWhiteSpace(r.Secondary))
            .WithMessage("Choose a secondary affiliation from the list.");
        RuleFor(r => r.SpecialUnit).MaximumLength(NameMaxLength);
        RuleFor(r => r.Prefix).Matches("^[A-Za-z0-9]{1,8}$")
            .When(r => !string.IsNullOrWhiteSpace(r.Prefix))
            .WithMessage("Use 1 to 8 letters or digits.");
        RuleFor(r => r.OperatingNumber).Matches("^[A-Za-z0-9]{1,16}$")
            .When(r => !string.IsNullOrWhiteSpace(r.OperatingNumber))
            .WithMessage("Use 1 to 16 letters or digits.");
        RuleFor(r => r.OperatingNumber).NotEmpty()
            .When(r => !string.IsNullOrWhiteSpace(r.Prefix))
            .WithMessage("Enter an operating number to go with the prefix.");
    }
}
