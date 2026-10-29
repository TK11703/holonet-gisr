using FluentValidation;
using Gisr.Shared.Accounts;

namespace Gisr.Api.Accounts;

public sealed class UpdateAccountSettingsRequestValidator : AbstractValidator<UpdateAccountSettingsRequest>
{
    public const int DisplayNameMaxLength = 100;
    public const int ContactEmailMaxLength = 254;

    public UpdateAccountSettingsRequestValidator()
    {
        RuleFor(r => r.DisplayName).NotEmpty().MaximumLength(DisplayNameMaxLength);

        RuleFor(r => r.ContactEmail!)
            .EmailAddress()
            .MaximumLength(ContactEmailMaxLength)
            .When(r => r.ContactEmail is not null)
            .OverridePropertyName(nameof(UpdateAccountSettingsRequest.ContactEmail));

        RuleFor(r => r.Theme!)
            .Must(SiteThemes.IsKnown)
            .WithMessage("Choose one of the available themes.")
            .When(r => r.Theme is not null)
            .OverridePropertyName(nameof(UpdateAccountSettingsRequest.Theme));
    }
}
