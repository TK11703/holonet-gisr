namespace Gisr.Shared.Records;

public sealed record RecordDto(
    Guid Id,
    string Title,
    string Affiliation,
    DateOnly? EngagementStart,
    DateOnly? EngagementEnd,
    IReadOnlyDictionary<string, string> EngagementDetail,
    IReadOnlyList<IncidentDto> Incidents,
    IReadOnlyList<AwardDto> Awards,
    Guid SubjectAccountId,
    string SubjectDisplayName,
    Guid AuthorAccountId,
    string AuthorDisplayName,
    bool CanEdit,
    bool IsSealed,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int ArtifactCount = 0,
    string? SubjectOperatingNumber = null);
