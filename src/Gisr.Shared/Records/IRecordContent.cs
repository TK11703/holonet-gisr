namespace Gisr.Shared.Records;

// Common shape of the create and update requests, so one validator ruleset covers both.
public interface IRecordContent
{
    string Title { get; }

    string Affiliation { get; }

    DateOnly? EngagementStart { get; }

    DateOnly? EngagementEnd { get; }

    IReadOnlyDictionary<string, string>? EngagementDetail { get; }

    IReadOnlyList<IncidentDto>? Incidents { get; }

    IReadOnlyList<AwardDto>? Awards { get; }

    bool IsSealed { get; }

    string? OverrideCode { get; }
}
