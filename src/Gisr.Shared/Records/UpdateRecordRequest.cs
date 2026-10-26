namespace Gisr.Shared.Records;

public sealed record UpdateRecordRequest(
    string Title,
    string Affiliation,
    DateOnly? EngagementStart,
    DateOnly? EngagementEnd,
    IReadOnlyDictionary<string, string>? EngagementDetail,
    IReadOnlyList<IncidentDto>? Incidents,
    IReadOnlyList<AwardDto>? Awards,
    int ExpectedVersion,
    bool IsSealed = false,
    string? OverrideCode = null) : IRecordContent;
