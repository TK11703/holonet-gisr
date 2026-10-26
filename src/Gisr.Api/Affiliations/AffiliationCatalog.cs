using Gisr.Shared.Affiliations;

namespace Gisr.Api.Affiliations;

public static class AffiliationCatalog
{
    public const string SummaryKey = "summary";
    public const int SummaryMaxLength = 4000;
    public const int TextMaxLength = 200;

    private static readonly FieldDefinitionDto Summary = new(SummaryKey, "Summary", "multiline", true, SummaryMaxLength);

    public static IReadOnlyList<AffiliationDto> All { get; } =
    [
        new("civilian", "Civilian",
        [
            Summary,
            Text("homeworld", "Homeworld"),
            Text("occupation", "Occupation"),
            Text("employer", "Employer"),
        ]),
        new("military", "Military",
        [
            Summary,
            Select("branch", "Branch", true, "Navy", "Army", "Starfighter Corps", "Marines", "Other"),
            Text("rank", "Rank", true),
            Text("unit", "Unit"),
            Text("service_number", "Service number"),
        ]),
        new("jedi", "Jedi",
        [
            Summary,
            Select("order_rank", "Order rank", true, "Youngling", "Padawan", "Knight", "Master"),
            Text("mentor", "Mentor"),
            Text("specialization", "Specialization"),
            Text("temple", "Temple"),
        ]),
        new("bounty_hunter", "Bounty Hunter",
        [
            Summary,
            Text("guild_registration", "Guild registration", true),
            Select("license_status", "License status", true, "Active", "Suspended", "Expired"),
            Text("contract_client", "Contract client"),
            Text("primary_target_category", "Primary target category"),
        ]),
    ];

    public static AffiliationDto? Find(string? code) =>
        All.FirstOrDefault(a => string.Equals(a.Code, code, StringComparison.Ordinal));

    private static FieldDefinitionDto Text(string key, string label, bool required = false) =>
        new(key, label, "text", required, TextMaxLength);

    private static FieldDefinitionDto Select(string key, string label, bool required, params string[] options) =>
        new(key, label, "select", required, null, options);
}
