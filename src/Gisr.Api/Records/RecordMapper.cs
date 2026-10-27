using System.Text.Json;
using Gisr.Database.Records;
using Gisr.Shared.Records;

namespace Gisr.Api.Records;

public static class RecordMapper
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static RecordDto ToDto(RecordAggregate aggregate, RecordCaller caller)
    {
        var row = aggregate.Record;
        return new RecordDto(
            row.Id,
            row.Title,
            row.Affiliation,
            row.EngagementStart,
            row.EngagementEnd,
            ParseDetail(row.EngagementDetail),
            [.. aggregate.Incidents.Select(i => new IncidentDto(i.Id, i.OccurredOn, i.Title, i.Description))],
            [.. aggregate.Awards.Select(a => new AwardDto(a.Id, a.Name, a.AwardedOn, a.Issuer, a.Description))],
            row.SubjectAccountId,
            row.SubjectDisplayName,
            row.AuthorAccountId,
            row.AuthorDisplayName,
            IsServiceRecord(row),
            RecordAccessPolicy.Edit(row, caller) == AccessDecision.Allowed,
            row.IsSealed,
            row.Version,
            row.CreatedAt,
            row.UpdatedAt,
            row.ArtifactCount);
    }

    public static UnlockedRecordDto ToUnlocked(RecordAggregate aggregate, RecordCaller caller, string viewToken, DateTimeOffset expiresAt) =>
        new(ToDto(aggregate, caller), viewToken, expiresAt);

    public static RecordSummaryDto ToSummary(RecordRow row) =>
        row.IsLocked
            ? new RecordSummaryDto(row.Id, null, null, null, null, null, IsServiceRecord(row), true, true, null, null, row.CreatedAt, null)
            : new RecordSummaryDto(
                row.Id,
                row.Title,
                row.Affiliation,
                row.SubjectAccountId,
                row.SubjectDisplayName,
                row.AuthorDisplayName,
                IsServiceRecord(row),
                row.IsSealed,
                false,
                row.EngagementStart,
                row.EngagementEnd,
                row.CreatedAt,
                row.UpdatedAt,
                row.ArtifactCount);

    public static RecordWrite ToWrite(IRecordContent content)
    {
        var detail = (content.EngagementDetail ?? new Dictionary<string, string>())
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Value))
            .ToDictionary(entry => entry.Key, entry => entry.Value.Trim(), StringComparer.Ordinal);

        return new RecordWrite(
            content.Title.Trim(),
            content.Affiliation.Trim(),
            content.EngagementStart,
            content.EngagementEnd,
            JsonSerializer.Serialize(detail, Json),
            [.. (content.Incidents ?? []).Select(i => new IncidentWrite(i.OccurredOn, i.Title.Trim(), NullIfBlank(i.Description)))],
            [.. (content.Awards ?? []).Select(a => new AwardWrite(a.Name.Trim(), a.AwardedOn, NullIfBlank(a.Issuer), NullIfBlank(a.Description)))]);
    }

    public static RevisionDto ToRevisionDto(RevisionRow row) =>
        new(row.Version, row.ChangedByDisplayName, row.ChangedAt, row.Affiliation, row.Title);

    private static bool IsServiceRecord(RecordRow row) => row.AuthorAccountId != row.SubjectAccountId;

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Dictionary<string, string> ParseDetail(string json)
    {
        var detail = new Dictionary<string, string>(StringComparer.Ordinal);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in document.RootElement.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String))
            {
                detail[property.Name] = property.Value.GetString()!;
            }
        }

        return detail;
    }
}
