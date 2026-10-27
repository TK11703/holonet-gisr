using Gisr.Shared.Accounts;
using Gisr.Shared.Records;

namespace Gisr.Web.Tests.Records;

internal static class Samples
{
    private static readonly DateTimeOffset Created = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    public static IReadOnlyList<AccountAffiliationDto> Affiliations { get; } =
    [
        new(Guid.NewGuid(), "Military", null, null, null, null, Created),
        new(Guid.NewGuid(), "Jedi", null, null, null, null, Created),
    ];

    public static RecordDto Record(
        Guid? id = null,
        string title = "Clone Wars service",
        string affiliation = "Military",
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
            new Dictionary<string, string> { ["summary"] = "Served with distinction." },
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

    public static RecordSummaryDto ListItem(Guid? id = null, string title = "Clone Wars service", string affiliation = "Military", bool service = false, Guid? subjectId = null, string subjectName = "Rex", bool isSealed = false, bool isLocked = false) =>
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
