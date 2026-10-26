using Gisr.Database.Records;
using Gisr.Database.Tests.Common;
using Npgsql;

namespace Gisr.Database.Tests.Records;

[Collection("postgres")]
public class RecordRepositoryTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static IncidentWrite Incident(string title, string date = "2020-05-04", string? description = null) =>
        new(DateOnly.Parse(date), title, description);

    private static AwardWrite Award(string name, string date = "2021-01-02") =>
        new(name, DateOnly.Parse(date), "Republic", "For valor");

    [Fact]
    public async Task Create_PersistsRecordIncidentsAndAwards_InOrder()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var owner = await RecordTestData.CreateAccountAsync(db, "Rex");
        var write = RecordTestData.Write(
            start: new DateOnly(2019, 1, 1),
            end: new DateOnly(2020, 1, 1),
            incidents: [Incident("Geonosis"), Incident("Umbara", description: "Betrayal")],
            awards: [Award("Medal of Honor"), Award("Service Star")]);

        var created = await RecordTestData.Records(db).CreateAsync(owner.Id, owner.Id, write, Ct);

        Assert.Equal(1, created.Record.Version);
        Assert.Equal("Clone Wars service", created.Record.Title);
        Assert.Equal("military", created.Record.Affiliation);
        Assert.Equal(new DateOnly(2019, 1, 1), created.Record.EngagementStart);
        Assert.Equal(new DateOnly(2020, 1, 1), created.Record.EngagementEnd);
        Assert.Equal(owner.Id, created.Record.SubjectAccountId);
        Assert.Equal(owner.Id, created.Record.AuthorAccountId);
        Assert.Equal("Rex", created.Record.SubjectDisplayName);
        Assert.Equal(TimeSpan.Zero, created.Record.CreatedAt.Offset);
        Assert.Equal(["Geonosis", "Umbara"], created.Incidents.Select(i => i.Title));
        Assert.Equal("Betrayal", created.Incidents[1].Description);
        Assert.Equal(["Medal of Honor", "Service Star"], created.Awards.Select(a => a.Name));
        Assert.Equal("Republic", created.Awards[0].Issuer);
    }

    [Fact]
    public async Task Create_IsOneTransaction_FailureLeavesNothingBehind()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var owner = await RecordTestData.CreateAccountAsync(db);
        var write = RecordTestData.Write(incidents: [Incident("ok"), Incident(new string('x', 201))]);

        await Assert.ThrowsAsync<PostgresException>(() => RecordTestData.Records(db).CreateAsync(owner.Id, owner.Id, write, Ct));

        Assert.Equal(0, await db.ScalarAsync<int>("SELECT count(*) FROM record"));
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT count(*) FROM record_incident"));
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT count(*) FROM record_revision"));
    }

    [Fact]
    public async Task Create_PreservesUnknownKeysInEngagementDetail()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var owner = await RecordTestData.CreateAccountAsync(db);
        var detail = """{"summary":"s","rank":"Captain","legacy_field":"kept"}""";

        var created = await RecordTestData.Records(db).CreateAsync(owner.Id, owner.Id, RecordTestData.Write(detail: detail), Ct);
        var reloaded = await RecordTestData.Records(db).GetAsync(created.Record.Id, owner.Id, Ct);

        using var json = System.Text.Json.JsonDocument.Parse(reloaded!.Record.EngagementDetail);
        Assert.Equal("kept", json.RootElement.GetProperty("legacy_field").GetString());
        Assert.Equal("Captain", json.RootElement.GetProperty("rank").GetString());
    }

    [Fact]
    public async Task Get_IsScopedToTheSubject()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var owner = await RecordTestData.CreateAccountAsync(db);
        var other = await RecordTestData.CreateAccountAsync(db);
        var created = await RecordTestData.Records(db).CreateAsync(owner.Id, owner.Id, RecordTestData.Write(), Ct);

        Assert.NotNull(await RecordTestData.Records(db).GetAsync(created.Record.Id, owner.Id, Ct));
        Assert.Null(await RecordTestData.Records(db).GetAsync(created.Record.Id, other.Id, Ct));
        Assert.Null(await RecordTestData.Records(db).GetAsync(Guid.NewGuid(), owner.Id, Ct));
    }

    [Fact]
    public async Task Update_WithMatchingVersion_IncrementsVersionAndReplacesCollections()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var owner = await RecordTestData.CreateAccountAsync(db);
        var repository = RecordTestData.Records(db);
        var created = await repository.CreateAsync(owner.Id, owner.Id,
            RecordTestData.Write(incidents: [Incident("old one"), Incident("old two")], awards: [Award("old award")]), Ct);

        var result = await repository.UpdateAsync(created.Record.Id, owner.Id, 1, owner.Id,
            RecordTestData.Write(title: "Renamed", incidents: [Incident("new one")], awards: []), Ct);

        Assert.Equal(RecordUpdateOutcome.Updated, result.Outcome);
        Assert.Equal(2, result.Record!.Record.Version);
        Assert.Equal("Renamed", result.Record.Record.Title);
        Assert.Equal(["new one"], result.Record.Incidents.Select(i => i.Title));
        Assert.Empty(result.Record.Awards);
        Assert.True(result.Record.Record.UpdatedAt >= created.Record.UpdatedAt);
        Assert.Equal(created.Record.CreatedAt, result.Record.Record.CreatedAt);
        Assert.Equal(1, await db.ScalarAsync<int>("SELECT count(*) FROM record_incident"));
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT count(*) FROM record_award"));
    }

    [Fact]
    public async Task Update_WithStaleVersion_ReportsConflictAndChangesNothing()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var owner = await RecordTestData.CreateAccountAsync(db);
        var repository = RecordTestData.Records(db);
        var created = await repository.CreateAsync(owner.Id, owner.Id, RecordTestData.Write(incidents: [Incident("keep")]), Ct);
        await repository.UpdateAsync(created.Record.Id, owner.Id, 1, owner.Id, RecordTestData.Write(title: "Second"), Ct);

        var stale = await repository.UpdateAsync(created.Record.Id, owner.Id, 1, owner.Id, RecordTestData.Write(title: "Stale"), Ct);

        Assert.Equal(RecordUpdateOutcome.Conflict, stale.Outcome);
        Assert.Null(stale.Record);
        var current = await repository.GetAsync(created.Record.Id, owner.Id, Ct);
        Assert.Equal("Second", current!.Record.Title);
        Assert.Equal(2, current.Record.Version);
        Assert.Equal(2, await db.ScalarAsync<int>("SELECT count(*) FROM record_revision"));
    }

    [Fact]
    public async Task Update_UnknownOrForeignRecord_ReportsNotFound()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var owner = await RecordTestData.CreateAccountAsync(db);
        var other = await RecordTestData.CreateAccountAsync(db);
        var repository = RecordTestData.Records(db);
        var created = await repository.CreateAsync(owner.Id, owner.Id, RecordTestData.Write(), Ct);

        var unknown = await repository.UpdateAsync(Guid.NewGuid(), owner.Id, 1, owner.Id, RecordTestData.Write(), Ct);
        var foreign = await repository.UpdateAsync(created.Record.Id, other.Id, 1, other.Id, RecordTestData.Write(title: "Hijack"), Ct);

        Assert.Equal(RecordUpdateOutcome.NotFound, unknown.Outcome);
        Assert.Equal(RecordUpdateOutcome.NotFound, foreign.Outcome);
        Assert.Equal("Clone Wars service", (await repository.GetAsync(created.Record.Id, owner.Id, Ct))!.Record.Title);
    }

    [Fact]
    public async Task Update_ConcurrentWithSameVersion_ExactlyOneWins()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var owner = await RecordTestData.CreateAccountAsync(db);
        var repository = RecordTestData.Records(db);
        var created = await repository.CreateAsync(owner.Id, owner.Id, RecordTestData.Write(), Ct);

        var results = await Task.WhenAll(
            repository.UpdateAsync(created.Record.Id, owner.Id, 1, owner.Id, RecordTestData.Write(title: "A"), Ct),
            repository.UpdateAsync(created.Record.Id, owner.Id, 1, owner.Id, RecordTestData.Write(title: "B"), Ct));

        Assert.Single(results, r => r.Outcome == RecordUpdateOutcome.Updated);
        Assert.Single(results, r => r.Outcome == RecordUpdateOutcome.Conflict);
    }

    [Fact]
    public async Task List_ReturnsOnlyTheSubjectsRecords_NewestUpdatedFirst()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var owner = await RecordTestData.CreateAccountAsync(db);
        var other = await RecordTestData.CreateAccountAsync(db);
        var repository = RecordTestData.Records(db);
        var first = await repository.CreateAsync(owner.Id, owner.Id, RecordTestData.Write(title: "First"), Ct);
        await repository.CreateAsync(owner.Id, owner.Id, RecordTestData.Write(title: "Second"), Ct);
        await repository.CreateAsync(other.Id, other.Id, RecordTestData.Write(title: "Not mine"), Ct);
        await repository.UpdateAsync(first.Record.Id, owner.Id, 1, owner.Id, RecordTestData.Write(title: "First"), Ct);

        var page = await repository.ListAsync(owner.Id, RecordListScope.Mine, null, null, RecordSort.UpdatedDesc, 1, 20, Ct);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(["First", "Second"], page.Items.Select(r => r.Title));
    }

    [Fact]
    public async Task List_FiltersByAffiliation()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var owner = await RecordTestData.CreateAccountAsync(db);
        var repository = RecordTestData.Records(db);
        await repository.CreateAsync(owner.Id, owner.Id, RecordTestData.Write(title: "Soldier", affiliation: "military"), Ct);
        await repository.CreateAsync(owner.Id, owner.Id, RecordTestData.Write(title: "Knight", affiliation: "jedi"), Ct);

        var jedi = await repository.ListAsync(owner.Id, RecordListScope.Mine, null, "jedi", RecordSort.UpdatedDesc, 1, 20, Ct);

        Assert.Equal(1, jedi.TotalCount);
        Assert.Equal("Knight", Assert.Single(jedi.Items).Title);
    }

    [Theory]
    [InlineData(RecordSort.TitleAsc, new[] { "Alpha", "Bravo", "Charlie" })]
    [InlineData(RecordSort.TitleDesc, new[] { "Charlie", "Bravo", "Alpha" })]
    [InlineData(RecordSort.UpdatedAsc, new[] { "Charlie", "Alpha", "Bravo" })]
    [InlineData(RecordSort.UpdatedDesc, new[] { "Bravo", "Alpha", "Charlie" })]
    public async Task List_AppliesSortOption(RecordSort sort, string[] expected)
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var owner = await RecordTestData.CreateAccountAsync(db);
        var repository = RecordTestData.Records(db);
        await repository.CreateAsync(owner.Id, owner.Id, RecordTestData.Write(title: "Charlie"), Ct);
        await repository.CreateAsync(owner.Id, owner.Id, RecordTestData.Write(title: "Alpha"), Ct);
        await repository.CreateAsync(owner.Id, owner.Id, RecordTestData.Write(title: "Bravo"), Ct);

        var page = await repository.ListAsync(owner.Id, RecordListScope.Mine, null, null, sort, 1, 20, Ct);

        Assert.Equal(expected, page.Items.Select(r => r.Title));
    }

    [Fact]
    public async Task List_Pages_AndReportsTotal()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var owner = await RecordTestData.CreateAccountAsync(db);
        var repository = RecordTestData.Records(db);
        foreach (var title in new[] { "A", "B", "C", "D", "E" })
        {
            await repository.CreateAsync(owner.Id, owner.Id, RecordTestData.Write(title: title), Ct);
        }

        var second = await repository.ListAsync(owner.Id, RecordListScope.Mine, null, null, RecordSort.TitleAsc, 2, 2, Ct);
        var third = await repository.ListAsync(owner.Id, RecordListScope.Mine, null, null, RecordSort.TitleAsc, 3, 2, Ct);
        var beyond = await repository.ListAsync(owner.Id, RecordListScope.Mine, null, null, RecordSort.TitleAsc, 4, 2, Ct);

        Assert.Equal(5, second.TotalCount);
        Assert.Equal(["C", "D"], second.Items.Select(r => r.Title));
        Assert.Equal(["E"], third.Items.Select(r => r.Title));
        Assert.Empty(beyond.Items);
        Assert.Equal(5, beyond.TotalCount);
    }

    [Fact]
    public async Task List_NewAccount_HasNoRecords()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var owner = await RecordTestData.CreateAccountAsync(db);

        var page = await RecordTestData.Records(db).ListAsync(owner.Id, RecordListScope.Mine, null, null, RecordSort.UpdatedDesc, 1, 20, Ct);

        Assert.Equal(0, page.TotalCount);
        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task Create_WithANotification_StoresItLinkedToTheNewRecord_InTheSameTransaction()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var subject = await RecordTestData.CreateAccountAsync(db, "Citizen");
        var officer = await RecordTestData.CreateAccountAsync(db, "Officer");
        var notification = new Gisr.Database.Notifications.NotificationWrite(
            subject.Id, Gisr.Database.Notifications.NotificationKind.ServiceRecordCreated, null, "A service record was created for you.");

        var created = await RecordTestData.Records(db).CreateAsync(subject.Id, officer.Id, RecordTestData.Write(), notification, Ct);

        Assert.Equal(1, await db.ScalarAsync<int>(
            $"SELECT count(*) FROM notification WHERE recipient_account_id = '{subject.Id}' AND record_id = '{created.Record.Id}'"));
        Assert.Equal(officer.Id, created.Record.AuthorAccountId);
        Assert.Equal(subject.Id, created.Record.SubjectAccountId);
    }

    [Fact]
    public async Task Create_WhenTheNotificationFails_RollsTheRecordBack()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var subject = await RecordTestData.CreateAccountAsync(db);
        var notification = new Gisr.Database.Notifications.NotificationWrite(
            Guid.NewGuid(), Gisr.Database.Notifications.NotificationKind.ServiceRecordCreated, null, "x");

        await Assert.ThrowsAsync<PostgresException>(() =>
            RecordTestData.Records(db).CreateAsync(subject.Id, subject.Id, RecordTestData.Write(), notification, Ct));

        Assert.Equal(0, await db.ScalarAsync<int>("SELECT count(*) FROM record"));
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT count(*) FROM record_revision"));
    }

    [Fact]
    public async Task Create_WithoutANotification_StoresNone()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var subject = await RecordTestData.CreateAccountAsync(db);

        await RecordTestData.Records(db).CreateAsync(subject.Id, subject.Id, RecordTestData.Write(), null, Ct);

        Assert.Equal(0, await db.ScalarAsync<int>("SELECT count(*) FROM notification"));
    }
}
