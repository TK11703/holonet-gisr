using Gisr.Shared.Records;

namespace Gisr.Web.Records;

public sealed class IncidentModel
{
    public DateOnly? OccurredOn { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }
}

public sealed class AwardModel
{
    public string Name { get; set; } = string.Empty;

    public DateOnly? AwardedOn { get; set; }

    public string? Issuer { get; set; }

    public string? Description { get; set; }
}

public sealed class RecordFormModel
{
    public const int MaxItems = 100;

    public string Title { get; set; } = string.Empty;

    public string Affiliation { get; set; } = string.Empty;

    public DateOnly? EngagementStart { get; set; }

    public DateOnly? EngagementEnd { get; set; }

    public Dictionary<string, string> Detail { get; } = new(StringComparer.Ordinal);

    public List<IncidentModel> Incidents { get; } = [];

    public List<AwardModel> Awards { get; } = [];

    public int ExpectedVersion { get; set; }

    public Guid? SubjectObjectId { get; set; }

    public string? SubjectDisplayName { get; set; }

    public bool IsSealed { get; set; }

    public string? OverrideCode { get; set; }

    // The existing code is never sent to the browser; this only records that one is set.
    public bool HasOverrideCode { get; set; }

    public bool ChangingCode { get; set; }

    public bool NeedsCode => IsSealed && (!HasOverrideCode || ChangingCode);

    public static RecordFormModel New(string affiliation) => new() { Affiliation = affiliation };

    public static RecordFormModel FromDto(RecordDto record)
    {
        var model = new RecordFormModel
        {
            Title = record.Title,
            Affiliation = record.Affiliation,
            EngagementStart = record.EngagementStart,
            EngagementEnd = record.EngagementEnd,
            ExpectedVersion = record.Version,
            IsSealed = record.IsSealed,
            HasOverrideCode = record.IsSealed,
        };

        foreach (var (key, value) in record.EngagementDetail)
        {
            model.Detail[key] = value;
        }

        model.Incidents.AddRange(record.Incidents.Select(i =>
            new IncidentModel { OccurredOn = i.OccurredOn, Title = i.Title, Description = i.Description }));
        model.Awards.AddRange(record.Awards.Select(a =>
            new AwardModel { Name = a.Name, AwardedOn = a.AwardedOn, Issuer = a.Issuer, Description = a.Description }));
        return model;
    }

    public CreateRecordRequest ToCreateRequest() =>
        new(Title, Affiliation, EngagementStart, EngagementEnd, new Dictionary<string, string>(Detail), ToIncidents(), ToAwards(), SubjectObjectId,
            IsSealed, IsSealed ? OverrideCode : null);

    public UpdateRecordRequest ToUpdateRequest() =>
        new(Title, Affiliation, EngagementStart, EngagementEnd, new Dictionary<string, string>(Detail), ToIncidents(), ToAwards(), ExpectedVersion,
            IsSealed, NeedsCode ? OverrideCode : null);

    private List<IncidentDto> ToIncidents() =>
        [.. Incidents.Select(i => new IncidentDto(null, i.OccurredOn ?? default, i.Title, i.Description))];

    private List<AwardDto> ToAwards() =>
        [.. Awards.Select(a => new AwardDto(null, a.Name, a.AwardedOn ?? default, a.Issuer, a.Description))];
}
