using FluentValidation;
using Gisr.Shared.Records;

namespace Gisr.Api.Records;

public abstract class RecordContentValidator<T> : AbstractValidator<T>
    where T : IRecordContent
{
    public const int TitleMaxLength = 200;
    public const int MaxIncidents = 100;
    public const int MaxAwards = 100;
    public const int AffiliationMaxLength = 320;
    public const int OverrideCodeMinLength = 8;
    public const int OverrideCodeMaxLength = 64;

    protected RecordContentValidator()
    {
        RuleFor(r => r.OverrideCode)
            .Must(code => !string.IsNullOrWhiteSpace(code) && code.Length is >= OverrideCodeMinLength and <= OverrideCodeMaxLength)
            .WithMessage($"'Override Code' must be between {OverrideCodeMinLength} and {OverrideCodeMaxLength} characters and not blank.")
            .When(r => r.OverrideCode is not null);
        RuleFor(r => r.OverrideCode)
            .Null()
            .WithMessage("'Override Code' can only be set when the record is sealed.")
            .When(r => !r.IsSealed);

        RuleFor(r => r.Title).NotEmpty().MaximumLength(TitleMaxLength);

        RuleFor(r => r.Affiliation).NotEmpty().MaximumLength(AffiliationMaxLength);

        RuleFor(r => r.EngagementEnd)
            .Must((r, end) => end >= r.EngagementStart)
            .When(r => r.EngagementStart.HasValue && r.EngagementEnd.HasValue)
            .WithMessage("'Engagement End' must not be before 'Engagement Start'.");

        RuleFor(r => r).Custom((request, context) =>
        {
            foreach (var (key, message) in EngagementDetailValidator.Validate(request.EngagementDetail))
            {
                context.AddFailure(key.Length == 0 ? "EngagementDetail" : $"EngagementDetail.{key}", message);
            }
        });

        RuleFor(r => r.Incidents)
            .Must(items => items!.Count <= MaxIncidents)
            .When(r => r.Incidents is not null)
            .WithMessage($"At most {MaxIncidents} incidents are allowed.");
        RuleForEach(r => r.Incidents!)
            .SetValidator(new IncidentDtoValidator())
            .When(r => r.Incidents is not null);

        RuleFor(r => r.Awards)
            .Must(items => items!.Count <= MaxAwards)
            .When(r => r.Awards is not null)
            .WithMessage($"At most {MaxAwards} awards are allowed.");
        RuleForEach(r => r.Awards!)
            .SetValidator(new AwardDtoValidator())
            .When(r => r.Awards is not null);
    }
}

public sealed class CreateRecordRequestValidator : RecordContentValidator<CreateRecordRequest>
{
    public CreateRecordRequestValidator() =>
        RuleFor(r => r.OverrideCode)
            .NotEmpty()
            .WithMessage("An override code is required to seal a record.")
            .When(r => r.IsSealed && r.OverrideCode is null);
}

public sealed class UpdateRecordRequestValidator : RecordContentValidator<UpdateRecordRequest>
{
    public UpdateRecordRequestValidator() =>
        RuleFor(r => r.ExpectedVersion).GreaterThanOrEqualTo(1);
}

public sealed class OverrideCodeRequestValidator : AbstractValidator<OverrideCodeRequest>
{
    public const int MaxLength = 64;

    public OverrideCodeRequestValidator() =>
        RuleFor(r => r.Code).NotEmpty().MaximumLength(MaxLength);
}
