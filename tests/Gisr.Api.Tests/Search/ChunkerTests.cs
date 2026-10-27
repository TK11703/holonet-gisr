using Gisr.Api.Search;
using Gisr.Database.Records;

namespace Gisr.Api.Tests.Search;

public class ChunkerTests
{
    private static string Words(int count, string prefix = "w") => string.Join(' ', Enumerable.Range(1, count).Select(i => $"{prefix}{i}"));

    [Fact]
    public void ShortText_IsASingleChunk()
    {
        var chunks = Chunker.Split("  The   temple\nguard  served well.  ");

        Assert.Equal(["The temple guard served well."], chunks);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\t ")]
    public void EmptyText_HasNoChunks(string text) => Assert.Empty(Chunker.Split(text));

    [Fact]
    public void LongText_IsSplitIntoChunksOfAbout500Tokens()
    {
        var chunks = Chunker.Split(Words(2000));

        Assert.True(chunks.Count > 4);
        foreach (var chunk in chunks.Take(chunks.Count - 1))
        {
            var tokens = Chunker.EstimateTokens(chunk);
            Assert.InRange(tokens, 450, 500);
        }

        Assert.All(chunks, chunk => Assert.True(Chunker.EstimateTokens(chunk) <= Chunker.TargetTokens));
    }

    [Fact]
    public void ConsecutiveChunks_OverlapByAbout50Tokens()
    {
        var chunks = Chunker.Split(Words(2000));

        for (var i = 1; i < chunks.Count; i++)
        {
            var previous = chunks[i - 1].Split(' ');
            var current = chunks[i].Split(' ');
            var overlap = 0;
            for (var size = Math.Min(previous.Length, current.Length); size > 0; size--)
            {
                if (previous.TakeLast(size).SequenceEqual(current.Take(size)))
                {
                    overlap = size;
                    break;
                }
            }

            Assert.InRange(Chunker.EstimateTokens(string.Join(' ', current.Take(overlap))), 45, 55);
        }
    }

    [Fact]
    public void TheChunks_CoverEveryWord_InOrder()
    {
        var text = Words(1234);

        var chunks = Chunker.Split(text);

        Assert.StartsWith("w1 ", chunks[0], StringComparison.Ordinal);
        Assert.EndsWith(" w1234", chunks[^1], StringComparison.Ordinal);
        var seen = new HashSet<string>(chunks.SelectMany(c => c.Split(' ')));
        Assert.Equal(1234, seen.Count);
    }

    [Fact]
    public void TheLastChunk_IsNotJustTheOverlap()
    {
        var chunks = Chunker.Split(Words(Chunker.WordsPerChunk + 10));

        Assert.Equal(2, chunks.Count);
        Assert.Contains("w" + (Chunker.WordsPerChunk + 10), chunks[1], StringComparison.Ordinal);
        Assert.True(chunks[1].Split(' ').Length > Chunker.OverlapWords);
    }

    [Fact]
    public void ExactlyOneChunkOfWords_StaysOneChunk() =>
        Assert.Single(Chunker.Split(Words(Chunker.WordsPerChunk)));

    [Fact]
    public void AnUnbrokenRunOfCharacters_IsBrokenUpSoNoChunkExplodes()
    {
        var chunks = Chunker.Split(new string('x', 20_000));

        Assert.True(chunks.Count >= 2);
        Assert.All(chunks, chunk => Assert.All(chunk.Split(' '), word => Assert.True(word.Length <= Chunker.MaxWordLength)));
        Assert.All(chunks, chunk => Assert.True(Chunker.EstimateTokens(chunk) <= Chunker.TargetTokens + 50));
    }
}

public class RecordTextBuilderTests
{
    private static RecordAggregate Aggregate(string detailJson = """{"summary":"Guarded the temple","rank":"Captain","unknown_key":"ignored value"}""") =>
        new(
            new RecordRow
            {
                Id = Guid.NewGuid(),
                Title = "Temple guard",
                Affiliation = "Republic Military \u203A Rogue Squadron",
                EngagementStart = new DateOnly(2019, 3, 4),
                EngagementEnd = new DateOnly(2020, 5, 6),
                EngagementDetail = detailJson,
            },
            [
                new IncidentRow { Title = "Siege of Geonosis", OccurredOn = new DateOnly(2020, 1, 2), Description = "Held the gate", SortOrder = 0 },
                new IncidentRow { Title = "Quiet watch", OccurredOn = new DateOnly(2020, 2, 3), Description = null, SortOrder = 1 },
            ],
            [new AwardRow { Name = "Medal of Honor", AwardedOn = new DateOnly(2021, 7, 8), Issuer = "Republic", Description = "For valor", SortOrder = 0 }]);

    [Fact]
    public void IncludesTheTitleAndTheAffiliation()
    {
        var text = RecordTextBuilder.Build(Aggregate());

        Assert.Contains("Temple guard", text, StringComparison.Ordinal);
        Assert.Contains("Affiliation: Republic Military \u203A Rogue Squadron", text, StringComparison.Ordinal);
    }

    [Fact]
    public void IncludesTheEngagementPeriod()
    {
        var text = RecordTextBuilder.Build(Aggregate());

        Assert.Contains("2019-03-04", text, StringComparison.Ordinal);
        Assert.Contains("2020-05-06", text, StringComparison.Ordinal);
    }

    [Fact]
    public void IncludesTheSummary()
    {
        var text = RecordTextBuilder.Build(Aggregate());

        Assert.Contains("Summary: Guarded the temple", text, StringComparison.Ordinal);
    }

    [Fact]
    public void LeavesOutDetailKeysOtherThanTheSummary()
    {
        var text = RecordTextBuilder.Build(Aggregate());

        Assert.DoesNotContain("ignored value", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Captain", text, StringComparison.Ordinal);
    }

    [Fact]
    public void IncludesIncidentsAndAwards_WithTheirDescriptions()
    {
        var text = RecordTextBuilder.Build(Aggregate());

        Assert.Contains("Siege of Geonosis", text, StringComparison.Ordinal);
        Assert.Contains("2020-01-02", text, StringComparison.Ordinal);
        Assert.Contains("Held the gate", text, StringComparison.Ordinal);
        Assert.Contains("Quiet watch", text, StringComparison.Ordinal);
        Assert.Contains("Medal of Honor", text, StringComparison.Ordinal);
        Assert.Contains("Republic", text, StringComparison.Ordinal);
        Assert.Contains("For valor", text, StringComparison.Ordinal);
    }

    [Fact]
    public void NeverIncludesPeopleOrIdentifiers()
    {
        var aggregate = Aggregate();
        aggregate.Record.SubjectDisplayName = "Rex Subject";
        aggregate.Record.AuthorDisplayName = "Officer Author";

        var text = RecordTextBuilder.Build(aggregate);

        Assert.DoesNotContain("Rex Subject", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Officer Author", text, StringComparison.Ordinal);
        Assert.DoesNotContain(aggregate.Record.Id.ToString(), text, StringComparison.Ordinal);
    }

    [Fact]
    public void ARecordWithoutOptionalParts_StillBuilds()
    {
        var aggregate = new RecordAggregate(
            new RecordRow { Title = "Plain", Affiliation = "Civilian", EngagementDetail = "{}" }, [], []);

        var text = RecordTextBuilder.Build(aggregate);

        Assert.Contains("Plain", text, StringComparison.Ordinal);
        Assert.Contains("Civilian", text, StringComparison.Ordinal);
    }
}
