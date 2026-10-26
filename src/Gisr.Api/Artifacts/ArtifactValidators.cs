using FluentValidation;

namespace Gisr.Api.Artifacts;

// Describes one upload after the file has been sniffed, so every rule can be checked in one place.
public sealed record ArtifactUpload(string FileName, long SizeBytes, bool IsImage, string? Description, int ActiveCount);

public sealed class ArtifactUploadValidator : AbstractValidator<ArtifactUpload>
{
    public ArtifactUploadValidator()
    {
        RuleFor(u => u.FileName)
            .NotEmpty().WithMessage("The file needs a name.")
            .MaximumLength(ArtifactLimits.MaxFileNameLength);

        RuleFor(u => u.SizeBytes)
            .InclusiveBetween(1, ArtifactLimits.MaxBytes)
            .WithMessage("Files must be between 1 byte and 25 MB.");

        RuleFor(u => u.Description)
            .NotEmpty().WithMessage("A description is required for images; it is used as the alt text.")
            .When(u => u.IsImage);
        RuleFor(u => u.Description)
            .MaximumLength(ArtifactLimits.MaxDescriptionLength);

        RuleFor(u => u.ActiveCount)
            .LessThan(ArtifactLimits.MaxActivePerRecord)
            .WithMessage($"Max {ArtifactLimits.MaxActivePerRecord} active artifacts per record.");
    }
}

public sealed class UpdateArtifactRequestValidator : AbstractValidator<Gisr.Shared.Artifacts.UpdateArtifactRequest>
{
    public UpdateArtifactRequestValidator()
    {
        RuleFor(r => r.Description)
            .NotEmpty().WithMessage("A description is required.")
            .MaximumLength(ArtifactLimits.MaxDescriptionLength);
    }
}

public sealed record ArtifactListQuery(string? Kind, int? Page, int? PageSize);

public sealed class ArtifactListQueryValidator : AbstractValidator<ArtifactListQuery>
{
    public static readonly IReadOnlyList<string> Kinds = ["all", "image"];

    public ArtifactListQueryValidator()
    {
        RuleFor(q => q.Kind)
            .Must(kind => Kinds.Contains(kind!, StringComparer.Ordinal))
            .WithMessage("'Kind' must be one of: " + string.Join(", ", Kinds) + ".")
            .When(q => q.Kind is not null);

        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);
    }
}
