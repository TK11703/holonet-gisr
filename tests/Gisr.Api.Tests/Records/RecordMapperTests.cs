using System.Text.Json;
using Gisr.Api.Records;
using Gisr.Database.Records;
using Gisr.Shared.Records;

namespace Gisr.Api.Tests.Records;

public class RecordMapperTests
{
    private static readonly Guid Subject = Guid.NewGuid();

    private static RecordRow Row(Guid? author = null, string detail = """{"summary":"Served","rank":"Captain"}""") => new()
    {
        Id = Guid.NewGuid(),
        SubjectAccountId = Subject,
        AuthorAccountId = author ?? Subject,
        Title = "Clone Wars service",
        Affiliation = "military",
        EngagementStart = new DateOnly(2019, 1, 1),
        EngagementEnd = new DateOnly(2020, 1, 1),
        EngagementDetail = detail,
        Version = 3,
        CreatedAt = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero),
        UpdatedByAccountId = Subject,
        SubjectDisplayName = "Rex",
        AuthorDisplayName = "Cody",
    };

    private static RecordAggregate Aggregate(RecordRow row) => new(
        row,
        [new IncidentRow { Id = Guid.NewGuid(), RecordId = row.Id, OccurredOn = new DateOnly(2020, 5, 4), Title = "Geonosis", Description = "First battle", SortOrder = 0 }],
        [new AwardRow { Id = Guid.NewGuid(), RecordId = row.Id, Name = "Medal", AwardedOn = new DateOnly(2021, 1, 2), Issuer = "Republic", Description = null, SortOrder = 0 }]);

    [Fact]
    public void ToDto_MapsRecordFields_IncidentsAndAwards()
    {
        var aggregate = Aggregate(Row());

        var dto = RecordMapper.ToDto(aggregate, Subject);

        Assert.Equal(aggregate.Record.Id, dto.Id);
        Assert.Equal("Clone Wars service", dto.Title);
        Assert.Equal("military", dto.Affiliation);
        Assert.Equal(new DateOnly(2019, 1, 1), dto.EngagementStart);
        Assert.Equal(new DateOnly(2020, 1, 1), dto.EngagementEnd);
        Assert.Equal(3, dto.Version);
        Assert.Equal("Rex", dto.SubjectDisplayName);
        Assert.Equal("Cody", dto.AuthorDisplayName);
        Assert.Equal(aggregate.Record.CreatedAt, dto.CreatedAt);
        Assert.Equal(aggregate.Record.UpdatedAt, dto.UpdatedAt);
        var incident = Assert.Single(dto.Incidents);
        Assert.Equal(aggregate.Incidents[0].Id, incident.Id);
        Assert.Equal("Geonosis", incident.Title);
        Assert.Equal(new DateOnly(2020, 5, 4), incident.OccurredOn);
        var award = Assert.Single(dto.Awards);
        Assert.Equal("Medal", award.Name);
        Assert.Equal("Republic", award.Issuer);
        Assert.Null(award.Description);
    }

    [Fact]
    public void ToDto_EngagementDetail_RoundTripsFromJson()
    {
        var dto = RecordMapper.ToDto(Aggregate(Row(detail: """{"summary":"Served","rank":"Captain","legacy":"kept"}""")), Subject);

        Assert.Equal(
            new Dictionary<string, string> { ["summary"] = "Served", ["rank"] = "Captain", ["legacy"] = "kept" },
            dto.EngagementDetail);
    }

    [Fact]
    public void ToDto_CarriesTheAuthorSeparatelyFromTheSubject()
    {
        var dto = RecordMapper.ToDto(Aggregate(Row(author: Guid.NewGuid())), Subject);

        Assert.Equal(Subject, dto.SubjectAccountId);
        Assert.NotEqual(dto.SubjectAccountId, dto.AuthorAccountId);
    }

    [Fact]
    public void CanEdit_FollowsTheAccessPolicy()
    {
        var own = Aggregate(Row());
        var service = Aggregate(Row(author: Guid.NewGuid()));
        var officer = new RecordCaller(Guid.NewGuid(), true);

        Assert.True(RecordMapper.ToDto(own, Subject).CanEdit);
        Assert.False(RecordMapper.ToDto(own, Guid.NewGuid()).CanEdit);
        Assert.False(RecordMapper.ToDto(service, Subject).CanEdit);
        Assert.True(RecordMapper.ToDto(service, officer).CanEdit);
    }

    [Fact]
    public void ToSummary_MapsListFields()
    {
        var row = Row(author: Guid.NewGuid());

        var dto = RecordMapper.ToSummary(row);

        Assert.Equal(row.Id, dto.Id);
        Assert.Equal("Clone Wars service", dto.Title);
        Assert.Equal("military", dto.Affiliation);
        Assert.Equal(Subject, dto.SubjectAccountId);
        Assert.Equal("Rex", dto.SubjectDisplayName);
        Assert.Equal("Cody", dto.AuthorDisplayName);
        Assert.Equal(row.UpdatedAt, dto.UpdatedAt);
    }

    [Fact]
    public void ToWrite_TrimsText_DropsBlankDetailValues_AndTreatsMissingCollectionsAsEmpty()
    {
        var request = new CreateRecordRequest(
            "  Title  ", "military", null, null,
            new Dictionary<string, string> { ["summary"] = "  text  ", ["rank"] = "Captain", ["unit"] = "   " });

        var write = RecordMapper.ToWrite(request);

        Assert.Equal("Title", write.Title);
        Assert.Empty(write.Incidents);
        Assert.Empty(write.Awards);
        using var json = JsonDocument.Parse(write.EngagementDetail);
        Assert.Equal("text", json.RootElement.GetProperty("summary").GetString());
        Assert.Equal("Captain", json.RootElement.GetProperty("rank").GetString());
        Assert.False(json.RootElement.TryGetProperty("unit", out _));
    }

    [Fact]
    public void ToWrite_MapsIncidentsAndAwards_EmptyOptionalTextBecomesNull()
    {
        var request = new CreateRecordRequest(
            "T", "jedi", new DateOnly(2020, 1, 1), new DateOnly(2021, 1, 1), new Dictionary<string, string>(),
            [new IncidentDto(Guid.NewGuid(), new DateOnly(2020, 2, 2), " Duel ", "  ")],
            [new AwardDto(null, " Medal ", new DateOnly(2020, 3, 3), "", " For valor ")]);

        var write = RecordMapper.ToWrite(request);

        Assert.Equal(new DateOnly(2020, 1, 1), write.EngagementStart);
        var incident = Assert.Single(write.Incidents);
        Assert.Equal("Duel", incident.Title);
        Assert.Null(incident.Description);
        var award = Assert.Single(write.Awards);
        Assert.Equal("Medal", award.Name);
        Assert.Null(award.Issuer);
        Assert.Equal("For valor", award.Description);
    }

    [Fact]
    public void ToRevisionDto_MapsHistoryFields()
    {
        var row = new RevisionRow
        {
            Version = 2,
            ChangedByDisplayName = "Cody",
            ChangedAt = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero),
            Title = "Old title",
            Affiliation = "jedi",
            Snapshot = "{}",
        };

        var dto = RecordMapper.ToRevisionDto(row);

        Assert.Equal(new RevisionDto(2, "Cody", row.ChangedAt, "jedi", "Old title"), dto);
    }
}
