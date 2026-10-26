using Gisr.Api.Artifacts;

namespace Gisr.Api.Tests.Artifacts;

public class ArtifactQueryValidatorTests
{
    private readonly ArtifactListQueryValidator _validator = new();

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("all", 1, 20)]
    [InlineData("image", 3, 100)]
    public void ValidQueriesPass(string? kind, int? page, int? pageSize) =>
        Assert.True(_validator.Validate(new ArtifactListQuery(kind, page, pageSize)).IsValid);

    [Theory]
    [InlineData("video")]
    [InlineData("")]
    [InlineData("IMAGE")]
    public void KindMustBeAllOrImage(string kind)
    {
        var result = _validator.Validate(new ArtifactListQuery(kind, null, null));

        Assert.Equal(nameof(ArtifactListQuery.Kind), Assert.Single(result.Errors).PropertyName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void PageMustBeAtLeastOne(int page) =>
        Assert.False(_validator.Validate(new ArtifactListQuery(null, page, null)).IsValid);

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void PageSizeMustBeBetween1And100(int pageSize) =>
        Assert.False(_validator.Validate(new ArtifactListQuery(null, null, pageSize)).IsValid);
}
