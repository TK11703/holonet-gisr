namespace Gisr.Shared.Records;

// When IsLocked is true (a sealed record the caller has not unlocked) only Id, IsServiceRecord, IsSealed,
// IsLocked, and CreatedAt are populated; every other value (including ArtifactCount) is null.
public sealed record RecordSummaryDto(
    Guid Id,
    string? Title,
    string? Affiliation,
    Guid? SubjectAccountId,
    string? SubjectDisplayName,
    string? AuthorDisplayName,
    bool IsServiceRecord,
    bool IsSealed,
    bool IsLocked,
    DateOnly? EngagementStart,
    DateOnly? EngagementEnd,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    int? ArtifactCount = null);
