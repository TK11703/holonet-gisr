using Gisr.Shared.Records;

namespace Gisr.Api.Records;

public static class EngagementDetailValidator
{
    public const int MaxEntries = 50;
    public const int UnknownKeyValueMaxLength = 4000;

    public static IEnumerable<(string Key, string Message)> Validate(IReadOnlyDictionary<string, string>? detail)
    {
        var values = detail ?? new Dictionary<string, string>();

        if (values.Count > MaxEntries)
        {
            yield return (string.Empty, $"At most {MaxEntries} engagement detail entries are allowed.");
        }

        var summary = values.TryGetValue(EngagementSummary.Key, out var raw) ? raw?.Trim() : null;
        if (string.IsNullOrEmpty(summary))
        {
            yield return (EngagementSummary.Key, "'Summary' is required.");
        }
        else if (summary.Length > EngagementSummary.MaxLength)
        {
            yield return (EngagementSummary.Key, $"'Summary' must be {EngagementSummary.MaxLength} characters or fewer.");
        }

        foreach (var (key, value) in values.Where(v => !string.Equals(v.Key, EngagementSummary.Key, StringComparison.Ordinal)))
        {
            if (value is { Length: > UnknownKeyValueMaxLength })
            {
                yield return (key, $"'{key}' must be {UnknownKeyValueMaxLength} characters or fewer.");
            }
        }
    }
}
