using System.Globalization;
using Gisr.Shared.Accounts;

namespace Gisr.Web.Records;

public static class RecordDisplay
{
    public const string AffiliationSeparator = " \u203A ";

    public static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string Instant(DateTimeOffset instant) => Date(DateOnly.FromDateTime(instant.UtcDateTime));

    public static string Engagement(DateOnly? start, DateOnly? end) => (start, end) switch
    {
        ({ } s, { } e) => $"{Date(s)} \u2013 {Date(e)}",
        ({ } s, null) => $"From {Date(s)}",
        (null, { } e) => $"Until {Date(e)}",
        _ => "\u2014",
    };

    // The text stored on a record: the account affiliation's primary, secondary, and special unit, as shown in account settings.
    public static string AffiliationLabel(AccountAffiliationDto affiliation) =>
        string.Join(AffiliationSeparator, new[] { affiliation.Primary, affiliation.Secondary, affiliation.SpecialUnit }.Where(part => !string.IsNullOrWhiteSpace(part)));

    public static IReadOnlyList<string> AffiliationLabels(IEnumerable<AccountAffiliationDto> affiliations) =>
        [.. affiliations.Select(AffiliationLabel).Distinct(StringComparer.Ordinal)];

    public static string? ErrorText(IReadOnlyDictionary<string, string[]> errors, string key) =>
        errors.TryGetValue(key, out var messages) ? string.Join(" ", messages) : null;
}
