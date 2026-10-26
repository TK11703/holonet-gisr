namespace Gisr.Database.Artifacts;

public sealed class ArtifactRow
{
    public Guid Id { get; set; }

    public Guid RecordId { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string Sha256 { get; set; } = string.Empty;

    public string BlobName { get; set; } = string.Empty;

    public string? ThumbnailBlobName { get; set; }

    public bool IsImage { get; set; }

    public string? Description { get; set; }

    public Guid UploadedByAccountId { get; set; }

    public string UploadedByDisplayName { get; set; } = string.Empty;

    public DateTimeOffset UploadedAt { get; set; }

    public DateTimeOffset? RemovedAt { get; set; }

    public Guid? RemovedByAccountId { get; set; }

    public string IndexStatus { get; set; } = string.Empty;

    public string? IndexDetail { get; set; }

    public bool IsLocked { get; set; }
}

public sealed record ArtifactWrite(
    Guid Id,
    Guid RecordId,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    string BlobName,
    string? ThumbnailBlobName,
    bool IsImage,
    string? Description,
    Guid UploadedByAccountId);
