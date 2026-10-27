using Gisr.Database.Records;
using Gisr.Database.Tests.Common;

namespace Gisr.Database.Tests.Records;

[Collection("postgres")]
public class RecordRepositoryOfficerTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private sealed record World(Guid Alice, Guid Bob, Guid Officer, Guid AliceRecord, Guid BobRecord);

    private async Task<(TestDatabase Db, World World)> SeedAsync()
    {
        var db = await postgres.CreateMigratedDatabaseAsync();
        var alice = await RecordTestData.CreateAccountAsync(db, "Alice");
        var bob = await RecordTestData.CreateAccountAsync(db, "Bob");
        var officer = await RecordTestData.CreateAccountAsync(db, "Officer");
        var records = RecordTestData.Records(db);
        var aliceRecord = await records.CreateAsync(alice.Id, alice.Id, RecordTestData.Write(title: "Alice record"), Ct);
        var bobRecord = await records.CreateAsync(bob.Id, bob.Id, RecordTestData.Write(title: "Bob record", affiliation: "jedi"), Ct);
        await records.CreateAsync(officer.Id, officer.Id, RecordTestData.Write(title: "Officer record"), Ct);
        return (db, new World(alice.Id, bob.Id, officer.Id, aliceRecord.Record.Id, bobRecord.Record.Id));
    }

    [Fact]
    public async Task Get_Officer_ReadsAnotherCitizensRecord_ButOthersCannot()
    {
        var (db, w) = await SeedAsync();
        await using var _ = db;
        var records = RecordTestData.Records(db);

        var asOfficer = await records.GetAsync(w.AliceRecord, new RecordCaller(w.Officer, true), Ct);
        var asOther = await records.GetAsync(w.AliceRecord, new RecordCaller(w.Bob), Ct);
        var officerFlagForSomeoneElse = await records.GetAsync(w.AliceRecord, new RecordCaller(w.Bob, false), Ct);

        Assert.Equal("Alice", asOfficer!.Record.SubjectDisplayName);
        Assert.Null(asOther);
        Assert.Null(officerFlagForSomeoneElse);
    }

    [Fact]
    public async Task Update_Officer_EditsAnotherCitizensRecord_AttributedToTheOfficer()
    {
        var (db, w) = await SeedAsync();
        await using var _ = db;
        var records = RecordTestData.Records(db);

        var result = await records.UpdateAsync(w.AliceRecord, new RecordCaller(w.Officer, true), 1, w.Officer,
            RecordTestData.Write(title: "Corrected by officer"), Ct);

        Assert.Equal(RecordUpdateOutcome.Updated, result.Outcome);
        Assert.Equal(w.Alice, result.Record!.Record.SubjectAccountId);
        Assert.Equal(w.Alice, result.Record.Record.AuthorAccountId);
        Assert.Equal(w.Officer, result.Record.Record.UpdatedByAccountId);
        var history = await RecordTestData.Revisions(db).ListAsync(w.AliceRecord, 1, 20, Ct);
        Assert.Equal("Officer", history.Items[0].ChangedByDisplayName);
        Assert.Equal(2, history.Items[0].Version);
    }

    [Fact]
    public async Task Update_NonOfficer_CannotEditAnotherCitizensRecord()
    {
        var (db, w) = await SeedAsync();
        await using var _ = db;
        var records = RecordTestData.Records(db);

        var result = await records.UpdateAsync(w.AliceRecord, new RecordCaller(w.Bob), 1, w.Bob, RecordTestData.Write(title: "Hijack"), Ct);

        Assert.Equal(RecordUpdateOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task List_AllScope_Officer_SeesEveryonesRecords()
    {
        var (db, w) = await SeedAsync();
        await using var _ = db;

        var page = await RecordTestData.Records(db).ListAsync(
            new RecordCaller(w.Officer, true), RecordListScope.All, null, null, RecordSort.TitleAsc, 1, 20, Ct);

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(["Alice record", "Bob record", "Officer record"], page.Items.Select(r => r.Title));
    }

    [Fact]
    public async Task List_AllScope_FiltersBySubjectAndAffiliation()
    {
        var (db, w) = await SeedAsync();
        await using var _ = db;
        var records = RecordTestData.Records(db);
        var officer = new RecordCaller(w.Officer, true);

        var bySubject = await records.ListAsync(officer, RecordListScope.All, w.Bob, null, RecordSort.TitleAsc, 1, 20, Ct);
        var byAffiliation = await records.ListAsync(officer, RecordListScope.All, null, new RecordAffiliationFilter("jedi"), RecordSort.TitleAsc, 1, 20, Ct);

        Assert.Equal(["Bob record"], bySubject.Items.Select(r => r.Title));
        Assert.Equal(1, bySubject.TotalCount);
        Assert.Equal(["Bob record"], byAffiliation.Items.Select(r => r.Title));
    }

    [Fact]
    public async Task List_MineScope_Officer_SeesOnlyTheirOwnRecords_EvenWithASubjectFilter()
    {
        var (db, w) = await SeedAsync();
        await using var _ = db;

        var page = await RecordTestData.Records(db).ListAsync(
            new RecordCaller(w.Officer, true), RecordListScope.Mine, w.Bob, null, RecordSort.TitleAsc, 1, 20, Ct);

        Assert.Equal(["Officer record"], page.Items.Select(r => r.Title));
    }

    [Fact]
    public async Task List_AllScope_NonOfficer_ReturnsNothing_BecauseSqlRequiresTheOfficerFlag()
    {
        var (db, w) = await SeedAsync();
        await using var _ = db;

        var page = await RecordTestData.Records(db).ListAsync(
            new RecordCaller(w.Alice), RecordListScope.All, null, null, RecordSort.TitleAsc, 1, 20, Ct);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }
}
