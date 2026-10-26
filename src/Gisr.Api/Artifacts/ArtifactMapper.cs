using Gisr.Database.Artifacts;
using Gisr.Shared.Artifacts;

namespace Gisr.Api.Artifacts;

public static class ArtifactMapper
{
    // Blob names and hashes stay server-side.
    public static ArtifactDto ToDto(ArtifactRow row) =>
        new(row.Id, row.RecordId, row.FileName, row.ContentType, row.SizeBytes, row.IsImage, row.Description,
            row.UploadedByDisplayName, row.UploadedAt, row.IndexStatus, row.IndexDetail);
}
