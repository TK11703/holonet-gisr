using Gisr.Api.Artifacts;
using Gisr.Database.Artifacts;
using Gisr.Shared.Artifacts;

namespace Gisr.Api.Tests.Artifacts;

public class ArtifactMapperTests
{
    private static ArtifactRow Row() => new()
    {
        Id = Guid.NewGuid(),
        RecordId = Guid.NewGuid(),
        FileName = "orders.pdf",
        ContentType = "application/pdf",
        SizeBytes = 2048,
        Sha256 = "deadbeef",
        BlobName = "secret/blob-name",
        ThumbnailBlobName = "secret/thumb-name",
        IsImage = false,
        Description = "Signed orders",
        UploadedByAccountId = Guid.NewGuid(),
        UploadedByDisplayName = "Obi-Wan Kenobi",
        UploadedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
        IndexStatus = "skipped",
        IndexDetail = "no_text",
    };

    [Fact]
    public void ToDto_CopiesTheVisibleFields()
    {
        var row = Row();

        var dto = ArtifactMapper.ToDto(row);

        Assert.Equal(new ArtifactDto(row.Id, row.RecordId, "orders.pdf", "application/pdf", 2048, false, "Signed orders",
            "Obi-Wan Kenobi", row.UploadedAt, "skipped", "no_text"), dto);
    }

    [Fact]
    public void ToDto_NeverExposesBlobNamesOrHashes()
    {
        var names = typeof(ArtifactDto).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain(names, n => n.Contains("Blob", StringComparison.Ordinal) || n.Contains("Sha", StringComparison.Ordinal) || n.Contains("Thumbnail", StringComparison.Ordinal));
        var json = System.Text.Json.JsonSerializer.Serialize(ArtifactMapper.ToDto(Row()));
        Assert.DoesNotContain("secret/", json, StringComparison.Ordinal);
        Assert.DoesNotContain("deadbeef", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ToDto_CarriesThePendingStatus_WithoutADetail()
    {
        var row = Row();
        row.IndexStatus = "pending";
        row.IndexDetail = null;

        var dto = ArtifactMapper.ToDto(row);

        Assert.Equal("pending", dto.IndexStatus);
        Assert.Null(dto.IndexDetail);
    }
}
