namespace Gisr.Shared.Gallery;

public sealed record GalleryGroupDto(
    Guid RecordId,
    string Title,
    string Affiliation,
    string SubjectDisplayName,
    int ImageCount,
    IReadOnlyList<Guid> CoverArtifactIds);
