namespace Gisr.Database.Records;

public sealed record IncidentWrite(DateOnly OccurredOn, string Title, string? Description);

public sealed record AwardWrite(string Name, DateOnly AwardedOn, string? Issuer, string? Description);

public sealed record RecordWrite(
    string Title,
    string Affiliation,
    DateOnly? EngagementStart,
    DateOnly? EngagementEnd,
    string EngagementDetail,
    IReadOnlyList<IncidentWrite> Incidents,
    IReadOnlyList<AwardWrite> Awards,
    bool IsSealed = false,
    string? OverrideCodeHash = null);

public sealed record RecordAggregate(RecordRow Record, IReadOnlyList<IncidentRow> Incidents, IReadOnlyList<AwardRow> Awards);

public enum RecordListScope
{
    Mine,
    All,
}

// Officer status comes from validated token claims; SQL only widens access when it is set.
// Unlocked is set only by the override flow after the subject proved the code.
public sealed record RecordCaller(Guid AccountId, bool IsOfficer = false, bool Unlocked = false)
{
    public static implicit operator RecordCaller(Guid accountId) => new(accountId);
}

public enum RecordSort
{
    UpdatedDesc,
    UpdatedAsc,
    TitleAsc,
    TitleDesc,
}

public enum RecordUpdateOutcome
{
    Updated,
    Conflict,
    NotFound,
}

public sealed record RecordUpdateResult(RecordUpdateOutcome Outcome, RecordAggregate? Record);
