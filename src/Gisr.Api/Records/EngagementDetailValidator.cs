using Gisr.Api.Affiliations;

namespace Gisr.Api.Records;

public static class EngagementDetailValidator
{
    public const int MaxEntries = 50;
    public const int UnknownKeyValueMaxLength = 4000;

    public static IEnumerable<(string Key, string Message)> Validate(string? affiliationCode, IReadOnlyDictionary<string, string>? detail)
    {
        var affiliation = AffiliationCatalog.Find(affiliationCode);
        if (affiliation is null)
        {
            yield break;
        }

        var values = detail ?? new Dictionary<string, string>();

        if (values.Count > MaxEntries)
        {
            yield return (string.Empty, $"At most {MaxEntries} engagement detail entries are allowed.");
        }

        foreach (var field in affiliation.Fields)
        {
            var value = values.TryGetValue(field.Key, out var raw) ? raw?.Trim() : null;
            if (string.IsNullOrEmpty(value))
            {
                if (field.Required)
                {
                    yield return (field.Key, $"'{field.Label}' is required.");
                }

                continue;
            }

            if (field.MaxLength is { } max && value.Length > max)
            {
                yield return (field.Key, $"'{field.Label}' must be {max} characters or fewer.");
            }

            if (field.Options is { } options && !options.Contains(value, StringComparer.Ordinal))
            {
                yield return (field.Key, $"'{field.Label}' must be one of: {string.Join(", ", options)}.");
            }
        }

        var known = affiliation.Fields.Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var (key, raw) in values.Where(v => !known.Contains(v.Key)))
        {
            if (raw is { Length: > UnknownKeyValueMaxLength })
            {
                yield return (key, $"'{key}' must be {UnknownKeyValueMaxLength} characters or fewer.");
            }
        }
    }
}
