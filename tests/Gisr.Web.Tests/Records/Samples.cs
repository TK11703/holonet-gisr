using Gisr.Shared.Affiliations;
using Gisr.Shared.Records;

namespace Gisr.Web.Tests.Records;

internal static class Samples
{
    public static readonly FieldDefinitionDto Summary = new("summary", "Summary", "multiline", true, 4000);

    public static IReadOnlyList<AffiliationDto> Affiliations { get; } =
    [
        new("military", "Military",
        [
            Summary,
            new("branch", "Branch", "select", true, null, ["Navy", "Army"]),
            new("rank", "Rank", "text", true, 200),
            new("unit", "Unit", "text", false, 200),
        ]),
        new("jedi", "Jedi",
        [
            Summary,
            new("order_rank", "Order rank", "select", true, null, ["Padawan", "Knight"]),
            new("mentor", "Mentor", "text", false, 200),
        ]),
    ];

    public static RecordDto Record(
        Guid? id = null,
        string title = "Clone Wars service",
        string affiliation = "military",
        bool canEdit = true,
        int version = 2,
        bool isSealed = false,
        bool isServiceRecord = false) =>
        new(
            id ?? Guid.NewGuid(),
            title,
            affiliation,
            new DateOnly(2019, 1, 1),
            new DateOnly(2020, 1, 1),
            new Dictionary<string, string> { ["summary"] = "Served with distinction.", ["branch"] = "Navy", ["rank"] = "Captain" },
            [new IncidentDto(Guid.NewGuid(), new DateOnly(2020, 5, 4), "Geonosis", "First battle")],
            [new AwardDto(Guid.NewGuid(), "Medal of Honor", new DateOnly(2021, 1, 2), "Republic", "For valor")],
            Guid.NewGuid(),
            "Rex",
            Guid.NewGuid(),
            "Rex",
            isServiceRecord,
            canEdit,
            isSealed,
            version,
            new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero));

    public static RecordSummaryDto ListItem(Guid? id = null, string title = "Clone Wars service", string affiliation = "military", bool service = false, Guid? subjectId = null, string subjectName = "Rex", bool isSealed = false, bool isLocked = false) =>
        new(
            id ?? Guid.NewGuid(),
            title,
            affiliation,
            subjectId ?? Guid.NewGuid(),
            subjectName,
            service ? "Officer Cody" : "Rex",
            service,
            isSealed || isLocked,
            isLocked,
            new DateOnly(2019, 1, 1),
            new DateOnly(2020, 1, 1),
            new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero));

    public static RecordSummaryDto LockedItem(Guid? id = null) =>
        new(id ?? Guid.NewGuid(), null, null, null, null, null, true, true, true, null, null, new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero), null);
}
