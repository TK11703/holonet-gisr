namespace Gisr.Shared.Artifacts;

public sealed record ArtifactDto(
    Guid Id,
    Guid RecordId,
    string FileName,
    string ContentType,
    long SizeBytes,
    bool IsImage,
    string? Description,
    string UploadedByDisplayName,
    DateTimeOffset UploadedAt,
    string IndexStatus,
    string? IndexDetail);
