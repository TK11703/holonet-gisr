using System.Text.RegularExpressions;

namespace Gisr.Web.Search;

public static class SearchHighlight
{
    private const int MinTermLength = 2;

    // Splits text into runs so matches can be wrapped in <mark> by the component; the text itself is never treated as markup.
    public static IReadOnlyList<(string Text, bool Match)> Split(string text, string query)
    {
        var terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(term => term.Length >= MinTermLength)
            .Select(Regex.Escape)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (terms.Length == 0 || text.Length == 0)
        {
            return [(text, false)];
        }

        var pattern = new Regex(string.Join('|', terms), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
        var runs = new List<(string, bool)>();
        var position = 0;
        try
        {
            foreach (Match match in pattern.Matches(text))
            {
                if (match.Index > position)
                {
                    runs.Add((text[position..match.Index], false));
                }

                runs.Add((match.Value, true));
                position = match.Index + match.Length;
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return [(text, false)];
        }

        if (position < text.Length)
        {
            runs.Add((text[position..], false));
        }

        return runs;
    }
}
