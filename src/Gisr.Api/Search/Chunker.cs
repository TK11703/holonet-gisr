using System.Text;
using System.Text.Json;
using Gisr.Database.Records;
using Gisr.Shared.Records;

namespace Gisr.Api.Search;

// Tokens are estimated without a tokenizer: a word costs 4/3 token, or one token per four characters when that is more
// (so unbroken runs of data cannot slip past the budget). Costs are kept in twelfths of a token to stay exact.
public static class Chunker
{
    public const int TargetTokens = 500;
    public const int OverlapTokens = 50;

    // What the budgets above come to for ordinary prose.
    public const int WordsPerChunk = 375;
    public const int OverlapWords = 38;
    public const int MaxWordLength = 200;

    private const int Twelfths = 12;

    public static int EstimateTokens(string text) =>
        (int)Math.Ceiling(text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Sum(word => (long)Cost(word)) / (double)Twelfths);

    public static IReadOnlyList<string> Split(string text)
    {
        var words = Words(text);
        if (words.Count == 0)
        {
            return [];
        }

        var costs = words.Select(Cost).ToArray();
        var chunks = new List<string>();
        var start = 0;
        while (true)
        {
            var end = start;
            var total = 0;
            while (end < words.Count && (end == start || total + costs[end] <= TargetTokens * Twelfths))
            {
                total += costs[end++];
            }

            chunks.Add(string.Join(' ', words.Skip(start).Take(end - start)));
            if (end == words.Count)
            {
                return chunks;
            }

            // The next chunk starts far enough back to repeat about OverlapTokens, but always moves forward.
            var next = end;
            var overlap = 0;
            while (next > start + 1 && overlap < OverlapTokens * Twelfths)
            {
                overlap += costs[--next];
            }

            start = next;
        }
    }

    private static int Cost(string word) => Math.Max(16, 3 * word.Length);

    // Unbroken runs (base64, minified data) are cut up so a single "word" cannot blow the token budget.
    private static List<string> Words(string text)
    {
        var words = new List<string>();
        foreach (var word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            for (var offset = 0; offset < word.Length; offset += MaxWordLength)
            {
                words.Add(word.Substring(offset, Math.Min(MaxWordLength, word.Length - offset)));
            }
        }

        return words;
    }
}
public static class RecordTextBuilder
{
    // People and identifiers are left out on purpose: only what a reader would search by meaning.
    public static string Build(RecordAggregate aggregate)
    {
        var record = aggregate.Record;
        var text = new StringBuilder();
        text.AppendLine($"Title: {record.Title}");
        text.AppendLine($"Affiliation: {record.Affiliation}");

        if (record.EngagementStart is not null || record.EngagementEnd is not null)
        {
            text.AppendLine($"Engagement: {Date(record.EngagementStart)} to {Date(record.EngagementEnd)}");
        }

        var detail = ParseDetail(record.EngagementDetail);
        if (detail.TryGetValue(EngagementSummary.Key, out var summary) && !string.IsNullOrWhiteSpace(summary))
        {
            text.AppendLine($"Summary: {summary}");
        }

        foreach (var incident in aggregate.Incidents.OrderBy(i => i.SortOrder))
        {
            text.AppendLine($"Incident {incident.OccurredOn:yyyy-MM-dd}: {incident.Title}{Suffix(incident.Description)}");
        }

        foreach (var award in aggregate.Awards.OrderBy(a => a.SortOrder))
        {
            var issuer = string.IsNullOrWhiteSpace(award.Issuer) ? string.Empty : $" from {award.Issuer}";
            text.AppendLine($"Award {award.AwardedOn:yyyy-MM-dd}: {award.Name}{issuer}{Suffix(award.Description)}");
        }

        return text.ToString();
    }

    private static string Date(DateOnly? date) => date is { } value ? value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : "present";

    private static string Suffix(string? description) => string.IsNullOrWhiteSpace(description) ? string.Empty : $" - {description}";

    private static Dictionary<string, string> ParseDetail(string json)
    {
        var detail = new Dictionary<string, string>(StringComparer.Ordinal);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in document.RootElement.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String))
            {
                detail[property.Name] = property.Value.GetString()!;
            }
        }

        return detail;
    }
}
