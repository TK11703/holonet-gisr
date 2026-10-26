using System.Text.Json;
using Gisr.Database.Records;
using Gisr.Database.Tests.Common;

namespace Gisr.Database.Tests.Records;

[Collection("postgres")]
public class RevisionRepositoryTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task Create_WritesRevisionOne_WithFullSnapshot()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var owner = await RecordTestData.CreateAccountAsync(db, "Obi-Wan");
        var write = RecordTestData.Write(
            title: "Temple service",
            affiliation: "jedi",
            detail: """{"summary":"Knight","order_rank":"Knight"}""",
            incidents: [new IncidentWrite(new DateOnly(2020, 5, 4), "Duel", "On Mustafar")],
            awards: [new AwardWrite("Council seat", new DateOnly(2021, 1, 2), "Jedi Council", null)]);
        var created = await RecordTestData.Records(db).CreateAsync(owner.Id, owner.Id, write, Ct);

        var history = await RecordTestData.Revisions(db).ListAsync(created.Record.Id, 1, 20, Ct);

        var revision = Assert.Single(history.Items);
        Assert.Equal(1, revision.Version);
        Assert.Equal("Obi-Wan", revision.ChangedByDisplayName);
        Assert.Equal("Temple service", revision.Title);
        Assert.Equal("jedi", revision.Affiliation);
        using var snapshot = JsonDocument.Parse(revision.Snapshot);
        var root = snapshot.RootElement;
        Assert.Equal("Temple service", root.GetProperty("title").GetString());
        Assert.Equal("Knight", root.GetProperty("engagementDetail").GetProperty("order_rank").GetString());
        Assert.Equal("Duel", root.GetProperty("incidents")[0].GetProperty("title").GetString());
        Assert.Equal("2020-05-04", root.GetProperty("incidents")[0].GetProperty("occurredOn").GetString());
        Assert.Equal("Council seat", root.GetProperty("awards")[0].GetProperty("name").GetString());
        Assert.Equal(1, root.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task EachUpdate_AddsOneRevision_NewestFirst_WithItsOwnSnapshot()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var owner = await RecordTestData.CreateAccountAsync(db);
        var editor = await RecordTestData.CreateAccountAsync(db, "Editor");
        var records = RecordTestData.Records(db);
        var created = await records.CreateAsync(owner.Id, owner.Id, RecordTestData.Write(title: "v1"), Ct);
        await records.UpdateAsync(created.Record.Id, owner.Id, 1, owner.Id, RecordTestData.Write(title: "v2"), Ct);
        await records.UpdateAsync(created.Record.Id, owner.Id, 2, editor.Id, RecordTestData.Write(title: "v3"), Ct);

        var history = await RecordTestData.Revisions(db).ListAsync(created.Record.Id, 1, 20, Ct);

        Assert.Equal(3, history.TotalCount);
        Assert.Equal([3, 2, 1], history.Items.Select(r => r.Version));
        Assert.Equal(["v3", "v2", "v1"], history.Items.Select(r => r.Title));
        Assert.Equal("Editor", history.Items[0].ChangedByDisplayName);
        using var latest = JsonDocument.Parse(history.Items[0].Snapshot);
        Assert.Equal("v3", latest.RootElement.GetProperty("title").GetString());
        Assert.Equal(3, latest.RootElement.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task History_Pages_AndIsScopedToTheRecord()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var owner = await RecordTestData.CreateAccountAsync(db);
        var records = RecordTestData.Records(db);
        var first = await records.CreateAsync(owner.Id, owner.Id, RecordTestData.Write(title: "a1"), Ct);
        var second = await records.CreateAsync(owner.Id, owner.Id, RecordTestData.Write(title: "b1"), Ct);
        await records.UpdateAsync(first.Record.Id, owner.Id, 1, owner.Id, RecordTestData.Write(title: "a2"), Ct);
        await records.UpdateAsync(first.Record.Id, owner.Id, 2, owner.Id, RecordTestData.Write(title: "a3"), Ct);

        var page1 = await RecordTestData.Revisions(db).ListAsync(first.Record.Id, 1, 2, Ct);
        var page2 = await RecordTestData.Revisions(db).ListAsync(first.Record.Id, 2, 2, Ct);
        var other = await RecordTestData.Revisions(db).ListAsync(second.Record.Id, 1, 20, Ct);

        Assert.Equal(3, page1.TotalCount);
        Assert.Equal([3, 2], page1.Items.Select(r => r.Version));
        Assert.Equal([1], page2.Items.Select(r => r.Version));
        Assert.Equal(1, other.TotalCount);
    }

    [Fact]
    public async Task FailedUpdate_DoesNotWriteARevision()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var owner = await RecordTestData.CreateAccountAsync(db);
        var records = RecordTestData.Records(db);
        var created = await records.CreateAsync(owner.Id, owner.Id, RecordTestData.Write(), Ct);

        await records.UpdateAsync(created.Record.Id, owner.Id, 9, owner.Id, RecordTestData.Write(title: "stale"), Ct);

        var history = await RecordTestData.Revisions(db).ListAsync(created.Record.Id, 1, 20, Ct);
        Assert.Equal(1, history.TotalCount);
    }
}
