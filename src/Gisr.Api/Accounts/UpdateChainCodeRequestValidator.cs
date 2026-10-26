using FluentValidation;
using Gisr.Shared.Accounts;

namespace Gisr.Api.Accounts;

public sealed class UpdateChainCodeRequestValidator : AbstractValidator<UpdateChainCodeRequest>
{
    public UpdateChainCodeRequestValidator()
    {
        RuleFor(profile => profile.FullName).MaximumLength(200);
        RuleFor(profile => profile.BirthDate).MaximumLength(200);
        RuleFor(profile => profile.Homeworld).MaximumLength(200);
        RuleFor(profile => profile.Species).MaximumLength(200);
        RuleFor(profile => profile.Height).MaximumLength(80);
        RuleFor(profile => profile.DistinguishingMarks).MaximumLength(1000);
        RuleFor(profile => profile.FamilyHistory).MaximumLength(4000);
        RuleFor(profile => profile.CriminalHistory).MaximumLength(4000);
        RuleFor(profile => profile.Biography).MaximumLength(4000);
        RuleFor(profile => profile.EyeColor).IsInEnum();
        RuleFor(profile => profile.SkinColor).IsInEnum();
        RuleFor(profile => profile.HairColor).IsInEnum();
        RuleFor(profile => profile.EyeColors).Must(ValidColors).WithMessage("Choose up to 20 valid eye colors.");
        RuleFor(profile => profile.SkinColors).Must(ValidColors).WithMessage("Choose up to 20 valid skin colors.");
        RuleFor(profile => profile.HairColors).Must(ValidColors).WithMessage("Choose up to 20 valid hair colors.");
        RuleFor(profile => profile).Must(profile => ChainCodeService.Normalize(profile) != new UpdateChainCodeRequest())
            .WithMessage("Enter at least one fictional profile detail.").OverridePropertyName("profile");
    }

    private static bool ValidColors(ProfileColor[]? colors) =>
        colors is null || (colors.Length <= Enum.GetValues<ProfileColor>().Length && colors.All(Enum.IsDefined));
}