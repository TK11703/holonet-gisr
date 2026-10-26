using FluentValidation;
using Gisr.Shared.Records;

namespace Gisr.Api.Records;

public sealed class IncidentDtoValidator : AbstractValidator<IncidentDto>
{
    public IncidentDtoValidator()
    {
        RuleFor(i => i.Title).NotEmpty().MaximumLength(200);
        RuleFor(i => i.OccurredOn).NotEqual(default(DateOnly)).WithMessage("'Occurred On' is required.");
        RuleFor(i => i.Description).MaximumLength(4000);
    }
}

public sealed class AwardDtoValidator : AbstractValidator<AwardDto>
{
    public AwardDtoValidator()
    {
        RuleFor(a => a.Name).NotEmpty().MaximumLength(200);
        RuleFor(a => a.AwardedOn).NotEqual(default(DateOnly)).WithMessage("'Awarded On' is required.");
        RuleFor(a => a.Issuer).MaximumLength(200);
        RuleFor(a => a.Description).MaximumLength(2000);
    }
}
