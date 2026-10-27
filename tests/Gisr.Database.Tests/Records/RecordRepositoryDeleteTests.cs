using Gisr.Database.Records;
using Gisr.Database.Tests.Common;

namespace Gisr.Database.Tests.Records;

[Collection("postgres")]
public class RecordRepositoryDeleteTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task Delete_HidesTheRecordFromGetAndList_ButKeepsRowAndRevisions()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var owner = await RecordTestData.CreateAccountAsync(db);
        var records = RecordTestData.Records(db);
        var kept = await records.CreateAsync(owner.Id, owner.Id, RecordTestData.Write(title: "Kept"), Ct);
        var doomed = await records.CreateAsync(owner.Id, owner.Id, RecordTestData.Write(title: "Doomed"), Ct);
        var caller = new RecordCaller(owner.Id);

        var deleted = await records.DeleteAsync(doomed.Record.Id, caller, owner.Id, Ct);

        Assert.True(deleted);
        Assert.Null(await records.GetAsync(doomed.Record.Id, caller, Ct));
        var list = await records.ListAsync(caller, RecordListScope.Mine, null, null, RecordSort.UpdatedDesc, 1, 20, Ct);
        Assert.Equal([kept.Record.Id], list.Items.Select(r => r.Id));
        Assert.Equal(1, list.TotalCount);
        Assert.Equal(1, await db.ScalarAsync<int>($"SELECT count(*) FROM record WHERE id = '{doomed.Record.Id}' AND deleted_at IS NOT NULL"));
        Assert.Equal(1, await db.ScalarAsync<int>($"SELECT count(*) FROM record_revision WHERE record_id = '{doomed.Record.Id}'"));
    }

    [Fact]
    public async Task Delete_ByAnotherNonOfficer_ChangesNothing()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var owner = await RecordTestData.CreateAccountAsync(db, "Rex");
        var other = await RecordTestData.CreateAccountAsync(db, "Cody");
        var records = RecordTestData.Records(db);
        var created = await records.CreateAsync(owner.Id, owner.Id, RecordTestData.Write(), Ct);

        var deleted = await records.DeleteAsync(created.Record.Id, new RecordCaller(other.Id), other.Id, Ct);

        Assert.False(deleted);
        Assert.NotNull(await records.GetAsync(created.Record.Id, new RecordCaller(owner.Id), Ct));
    }

    [Fact]
    public async Task Delete_ByAnOfficer_Works_AndASecondDeleteReportsNothingDeleted()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var owner = await RecordTestData.CreateAccountAsync(db, "Rex");
        var officer = await RecordTestData.CreateAccountAsync(db, "Officer");
        var records = RecordTestData.Records(db);
        var created = await records.CreateAsync(owner.Id, owner.Id, RecordTestData.Write(), Ct);
        var caller = new RecordCaller(officer.Id, true);

        Assert.True(await records.DeleteAsync(created.Record.Id, caller, officer.Id, Ct));
        Assert.False(await records.DeleteAsync(created.Record.Id, caller, officer.Id, Ct));
        Assert.Equal(1, await db.ScalarAsync<int>($"SELECT count(*) FROM record WHERE id = '{created.Record.Id}' AND deleted_by_account_id = '{officer.Id}'"));
    }

    [Fact]
    public async Task Update_OfADeletedRecord_ReportsNotFound()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var owner = await RecordTestData.CreateAccountAsync(db);
        var records = RecordTestData.Records(db);
        var created = await records.CreateAsync(owner.Id, owner.Id, RecordTestData.Write(), Ct);
        var caller = new RecordCaller(owner.Id);
        await records.DeleteAsync(created.Record.Id, caller, owner.Id, Ct);

        var result = await records.UpdateAsync(created.Record.Id, caller, created.Record.Version, owner.Id, RecordTestData.Write(title: "Renamed"), Ct);

        Assert.Equal(RecordUpdateOutcome.NotFound, result.Outcome);
    }
}
