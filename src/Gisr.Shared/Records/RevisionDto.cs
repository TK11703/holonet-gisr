namespace Gisr.Shared.Records;

public sealed record RevisionDto(int Version, string ChangedByDisplayName, DateTimeOffset ChangedAt, string Affiliation, string Title);
