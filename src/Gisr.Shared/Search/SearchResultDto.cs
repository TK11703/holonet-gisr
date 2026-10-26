namespace Gisr.Shared.Search;

public sealed record SearchResultDto(
    Guid RecordId,
    string RecordTitle,
    string Affiliation,
    string SubjectDisplayName,
    bool IsSealed,
    string SourceType,
    Guid? ArtifactId,
    string? ArtifactFileName,
    string Snippet,
    double Score);
