namespace Gisr.Shared.Records;

public sealed record CreateRecordRequest(
    string Title,
    string Affiliation,
    DateOnly? EngagementStart,
    DateOnly? EngagementEnd,
    IReadOnlyDictionary<string, string>? EngagementDetail,
    IReadOnlyList<IncidentDto>? Incidents = null,
    IReadOnlyList<AwardDto>? Awards = null,
    Guid? SubjectObjectId = null,
    bool IsSealed = false,
    string? OverrideCode = null) : IRecordContent;
