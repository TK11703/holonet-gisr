using Gisr.Api.Gallery;

namespace Gisr.Api.Tests.Gallery;

public class GalleryQueryValidatorTests
{
    private readonly GalleryQueryValidator _validator = new();

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("mine", 1, 12)]
    [InlineData("all", 5, 100)]
    public void ValidQueriesPass(string? scope, int? page, int? pageSize) =>
        Assert.True(_validator.Validate(new GalleryQuery(scope, page, pageSize)).IsValid);

    [Theory]
    [InlineData("updatedDesc")]
    [InlineData("titleAsc")]
    [InlineData("titleDesc")]
    public void KnownSortsPass(string sort) =>
        Assert.True(_validator.Validate(new GalleryQuery(null, null, null, sort)).IsValid);

    [Theory]
    [InlineData("updatedAsc")]
    [InlineData("")]
    [InlineData("TITLEASC")]
    public void SortMustBeKnown(string sort)
    {
        var result = _validator.Validate(new GalleryQuery(null, null, null, sort));

        Assert.Equal(nameof(GalleryQuery.Sort), Assert.Single(result.Errors).PropertyName);
    }

    [Theory]
    [InlineData("everyone")]
    [InlineData("")]
    [InlineData("ALL")]
    public void ScopeMustBeMineOrAll(string scope)
    {
        var result = _validator.Validate(new GalleryQuery(scope, null, null));

        Assert.Equal(nameof(GalleryQuery.Scope), Assert.Single(result.Errors).PropertyName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void PageMustBeAtLeastOne(int page) =>
        Assert.False(_validator.Validate(new GalleryQuery(null, page, null)).IsValid);

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void PageSizeMustBeBetween1And100(int pageSize) =>
        Assert.False(_validator.Validate(new GalleryQuery(null, null, pageSize)).IsValid);
}
