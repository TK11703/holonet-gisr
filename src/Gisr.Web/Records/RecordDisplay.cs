using System.Globalization;
using Gisr.Shared.Affiliations;

namespace Gisr.Web.Records;

public static class RecordDisplay
{
    public static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string Instant(DateTimeOffset instant) => Date(DateOnly.FromDateTime(instant.UtcDateTime));

    public static string Engagement(DateOnly? start, DateOnly? end) => (start, end) switch
    {
        ({ } s, { } e) => $"{Date(s)} \u2013 {Date(e)}",
        ({ } s, null) => $"From {Date(s)}",
        (null, { } e) => $"Until {Date(e)}",
        _ => "\u2014",
    };

    public static string AffiliationName(IReadOnlyList<AffiliationDto> affiliations, string code) =>
        affiliations.FirstOrDefault(a => string.Equals(a.Code, code, StringComparison.Ordinal))?.Name ?? code;

    public static string? ErrorText(IReadOnlyDictionary<string, string[]> errors, string key) =>
        errors.TryGetValue(key, out var messages) ? string.Join(" ", messages) : null;
}
