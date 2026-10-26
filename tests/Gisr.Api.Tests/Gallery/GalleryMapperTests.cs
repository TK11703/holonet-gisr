using Gisr.Api.Gallery;
using Gisr.Database.Gallery;
using Gisr.Shared.Gallery;

namespace Gisr.Api.Tests.Gallery;

public class GalleryMapperTests
{
    private static GalleryGroupRow Row(int covers = 4, int imageCount = 9) => new()
    {
        RecordId = Guid.NewGuid(),
        Title = "Clone Wars service",
        Affiliation = "military",
        SubjectDisplayName = "Rex",
        ImageCount = imageCount,
        CoverArtifactIds = [.. Enumerable.Range(0, covers).Select(_ => Guid.NewGuid())],
    };

    [Fact]
    public void ToDto_CopiesTheGroupFields()
    {
        var row = Row(3, 3);

        var dto = GalleryMapper.ToDto(row);

        Assert.Equal(new GalleryGroupDto(row.RecordId, "Clone Wars service", "military", "Rex", 3, row.CoverArtifactIds), dto, new GroupComparer());
    }

    [Fact]
    public void ToDto_CapsTheCoversAtFour_WhileKeepingTheTotalImageCount()
    {
        var row = Row(covers: 7, imageCount: 7);

        var dto = GalleryMapper.ToDto(row);

        Assert.Equal(4, dto.CoverArtifactIds.Count);
        Assert.Equal(row.CoverArtifactIds.Take(4), dto.CoverArtifactIds);
        Assert.Equal(7, dto.ImageCount);
    }

    [Fact]
    public void ToDto_NeverExposesBlobNamesOrHashes()
    {
        var names = typeof(GalleryGroupDto).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain(names, n => n.Contains("Blob", StringComparison.Ordinal) || n.Contains("Sha", StringComparison.Ordinal));
        Assert.Equal(["RecordId", "Title", "Affiliation", "SubjectDisplayName", "ImageCount", "CoverArtifactIds"], names);
    }

    private sealed class GroupComparer : IEqualityComparer<GalleryGroupDto>
    {
        public bool Equals(GalleryGroupDto? x, GalleryGroupDto? y) =>
            x is not null && y is not null
            && x.RecordId == y.RecordId && x.Title == y.Title && x.Affiliation == y.Affiliation
            && x.SubjectDisplayName == y.SubjectDisplayName && x.ImageCount == y.ImageCount
            && x.CoverArtifactIds.SequenceEqual(y.CoverArtifactIds);

        public int GetHashCode(GalleryGroupDto obj) => obj.RecordId.GetHashCode();
    }
}
