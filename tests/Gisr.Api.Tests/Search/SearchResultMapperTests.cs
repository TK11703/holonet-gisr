using Gisr.Api.Search;
using Gisr.Database.Search;

namespace Gisr.Api.Tests.Search;

public class SearchResultMapperTests
{
    private static SearchHitRow Hit(double distance = 0.25, string content = "snippet text", string source = "record", bool isSealed = false) => new()
    {
        RecordId = Guid.NewGuid(),
        RecordTitle = "Title",
        Affiliation = "military",
        SubjectDisplayName = "Rex",
        IsSealed = isSealed,
        SourceType = source,
        ArtifactId = source == "artifact" ? Guid.NewGuid() : null,
        ArtifactFileName = source == "artifact" ? "orders.pdf" : null,
        Content = content,
        Distance = distance,
    };

    [Fact]
    public void ToDto_CopiesTheHitFields()
    {
        var row = Hit();

        var dto = SearchResultMapper.ToDto(row, isOfficer: false);

        Assert.Equal(row.RecordId, dto.RecordId);
        Assert.Equal("Title", dto.RecordTitle);
        Assert.Equal("military", dto.Affiliation);
        Assert.Equal("Rex", dto.SubjectDisplayName);
        Assert.Equal("record", dto.SourceType);
        Assert.Equal("snippet text", dto.Snippet);
        Assert.Null(dto.ArtifactId);
        Assert.Null(dto.ArtifactFileName);
    }

    [Fact]
    public void TheSnippet_IsTruncatedToAtMost500Characters()
    {
        var dto = SearchResultMapper.ToDto(Hit(content: new string('a', 900)), isOfficer: false);

        Assert.Equal(500, dto.Snippet.Length);
        Assert.EndsWith("\u2026", dto.Snippet, StringComparison.Ordinal);
        Assert.Equal(500, SearchResultMapper.ToDto(Hit(content: new string('b', 500)), false).Snippet.Length);
        Assert.DoesNotContain("\u2026", SearchResultMapper.ToDto(Hit(content: new string('b', 500)), false).Snippet, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(0.25, 0.75)]
    [InlineData(1.0, 0.0)]
    [InlineData(1.6, 0.0)]
    [InlineData(-0.2, 1.0)]
    public void TheScore_IsTheCosineSimilarity_ClampedToZeroAndOne(double distance, double expected) =>
        Assert.Equal(expected, SearchResultMapper.ToDto(Hit(distance), false).Score, 6);

    [Fact]
    public void SealedIsOnlyReportedToOfficers()
    {
        var sealedHit = Hit(isSealed: true);

        Assert.True(SearchResultMapper.ToDto(sealedHit, isOfficer: true).IsSealed);
        Assert.False(SearchResultMapper.ToDto(sealedHit, isOfficer: false).IsSealed);
        Assert.False(SearchResultMapper.ToDto(Hit(isSealed: false), isOfficer: true).IsSealed);
    }

    [Fact]
    public void TheFileName_IsOnlyForArtifactHits()
    {
        var record = SearchResultMapper.ToDto(Hit(source: "record"), false);
        var artifact = SearchResultMapper.ToDto(Hit(source: "artifact"), false);

        Assert.Null(record.ArtifactFileName);
        Assert.Equal("orders.pdf", artifact.ArtifactFileName);
        Assert.NotNull(artifact.ArtifactId);
    }
}

public class SearchQueryValidatorTests
{
    private readonly SearchQueryValidator _validator = new();

    [Theory]
    [InlineData("ab", null, null, null, null)]
    [InlineData("  temple guard  ", "mine", "Republic Military", 1, 10)]
    [InlineData("temple", "all", null, 3, 50)]
    public void ValidQueriesPass(string q, string? scope, string? affiliation, int? page, int? pageSize) =>
        Assert.True(_validator.Validate(new SearchQuery(q, scope, affiliation, page, pageSize)).IsValid);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" a ")]
    [InlineData("   ")]
    public void TheQueryNeeds2CharactersAfterTrimming(string? q) =>
        Assert.Contains(nameof(SearchQuery.Q), _validator.Validate(new SearchQuery(q, null, null, null, null)).Errors.Select(e => e.PropertyName));

    [Fact]
    public void TheQueryIsCappedAt500CharactersAfterTrimming()
    {
        Assert.False(_validator.Validate(new SearchQuery(new string('a', 501), null, null, null, null)).IsValid);
        Assert.True(_validator.Validate(new SearchQuery("  " + new string('a', 500) + "  ", null, null, null, null)).IsValid);
    }

    [Theory]
    [InlineData("everyone", null, null, null)]
    [InlineData(null, "", null, null)]
    [InlineData(null, null, 0, null)]
    [InlineData(null, null, null, 0)]
    [InlineData(null, null, null, 51)]
    public void OtherParametersAreValidated(string? scope, string? affiliation, int? page, int? pageSize) =>
        Assert.False(_validator.Validate(new SearchQuery("temple", scope, affiliation, page, pageSize)).IsValid);
}
