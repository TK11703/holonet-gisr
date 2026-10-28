namespace Gisr.Database.Records;

public sealed class RecordRow
{
    public Guid Id { get; set; }

    public Guid SubjectAccountId { get; set; }

    public Guid AuthorAccountId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Affiliation { get; set; } = string.Empty;

    public DateOnly? EngagementStart { get; set; }

    public DateOnly? EngagementEnd { get; set; }

    public string EngagementDetail { get; set; } = "{}";

    public int Version { get; set; }

    public bool IsSealed { get; set; }

    public bool IsLocked { get; set; }

    public int ArtifactCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid UpdatedByAccountId { get; set; }

    public string SubjectDisplayName { get; set; } = string.Empty;

    public string? SubjectOperatingNumber { get; set; }

    public string AuthorDisplayName { get; set; } = string.Empty;
}

public sealed class IncidentRow
{
    public Guid Id { get; set; }

    public Guid RecordId { get; set; }

    public DateOnly OccurredOn { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public int SortOrder { get; set; }
}

public sealed class AwardRow
{
    public Guid Id { get; set; }

    public Guid RecordId { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateOnly AwardedOn { get; set; }

    public string? Issuer { get; set; }

    public string? Description { get; set; }

    public int SortOrder { get; set; }
}

public sealed class RevisionRow
{
    public Guid Id { get; set; }

    public Guid RecordId { get; set; }

    public int Version { get; set; }

    public Guid ChangedByAccountId { get; set; }

    public string ChangedByDisplayName { get; set; } = string.Empty;

    public DateTimeOffset ChangedAt { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Affiliation { get; set; } = string.Empty;

    public string Snapshot { get; set; } = "{}";
}
