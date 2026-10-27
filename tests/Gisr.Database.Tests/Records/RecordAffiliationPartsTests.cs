using Gisr.Database.Records;
using Gisr.Database.Tests.Common;

namespace Gisr.Database.Tests.Records;

[Collection("postgres")]
public class RecordAffiliationPartsTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private const string Sep = " \u203A ";

    [Fact]
    public async Task TheStoredAffiliationIsSplitIntoPrimaryAndSecondary()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var owner = await RecordTestData.CreateAccountAsync(db);
        var records = RecordTestData.Records(db);
        await records.CreateAsync(owner.Id, owner.Id, RecordTestData.Write(title: "Both", affiliation: "Jedi Order" + Sep + "Knight"), Ct);
        await records.CreateAsync(owner.Id, owner.Id, RecordTestData.Write(title: "Primary only", affiliation: "Rebel Alliance"), Ct);

        Assert.Equal(1, await db.ScalarAsync<int>("SELECT count(*) FROM record WHERE affiliation_primary = 'Jedi Order' AND affiliation_secondary = 'Knight'"));
        Assert.Equal(1, await db.ScalarAsync<int>("SELECT count(*) FROM record WHERE affiliation_primary = 'Rebel Alliance' AND affiliation_secondary IS NULL"));
    }

    [Fact]
    public async Task List_FiltersByPrimaryAndSecondary()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var owner = await RecordTestData.CreateAccountAsync(db);
        var records = RecordTestData.Records(db);
        await records.CreateAsync(owner.Id, owner.Id, RecordTestData.Write(title: "Knight", affiliation: "Jedi Order" + Sep + "Knight"), Ct);
        await records.CreateAsync(owner.Id, owner.Id, RecordTestData.Write(title: "Padawan", affiliation: "Jedi Order" + Sep + "Padawan"), Ct);
        await records.CreateAsync(owner.Id, owner.Id, RecordTestData.Write(title: "Rebel", affiliation: "Rebel Alliance"), Ct);

        var jedi = await records.ListAsync(owner.Id, RecordListScope.Mine, null, new RecordAffiliationFilter("Jedi Order"), RecordSort.TitleAsc, 1, 20, Ct);
        var knight = await records.ListAsync(owner.Id, RecordListScope.Mine, null, new RecordAffiliationFilter("Jedi Order", "Knight"), RecordSort.TitleAsc, 1, 20, Ct);

        Assert.Equal(["Knight", "Padawan"], jedi.Items.Select(r => r.Title));
        Assert.Equal(2, jedi.TotalCount);
        Assert.Equal(["Knight"], knight.Items.Select(r => r.Title));
        Assert.Equal(1, knight.TotalCount);
    }

    [Fact]
    public async Task Options_ListDistinctValues_NarrowSecondariesByPrimary_AndSkipDeletedRecords()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var owner = await RecordTestData.CreateAccountAsync(db);
        var records = RecordTestData.Records(db);
        await records.CreateAsync(owner.Id, owner.Id, RecordTestData.Write(title: "A", affiliation: "Jedi Order" + Sep + "Knight"), Ct);
        await records.CreateAsync(owner.Id, owner.Id, RecordTestData.Write(title: "B", affiliation: "Jedi Order" + Sep + "Knight"), Ct);
        await records.CreateAsync(owner.Id, owner.Id, RecordTestData.Write(title: "C", affiliation: "Galactic Empire" + Sep + "Imperial Navy"), Ct);
        var gone = await records.CreateAsync(owner.Id, owner.Id, RecordTestData.Write(title: "D", affiliation: "Sith Order" + Sep + "Apprentice"), Ct);
        await records.DeleteAsync(gone.Record.Id, new RecordCaller(owner.Id), owner.Id, Ct);

        var all = await records.ListAffiliationsAsync(new RecordCaller(owner.Id), RecordListScope.Mine, null, null, Ct);
        var empire = await records.ListAffiliationsAsync(new RecordCaller(owner.Id), RecordListScope.Mine, null, "Galactic Empire", Ct);

        Assert.Equal(["Galactic Empire", "Jedi Order"], all.Primaries);
        Assert.Equal(["Imperial Navy", "Knight"], all.Secondaries);
        Assert.Equal(["Imperial Navy"], empire.Secondaries);
    }

    [Fact]
    public async Task Options_LeaveOutLockedSealedRecords()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var subject = await RecordTestData.CreateAccountAsync(db, "Subject");
        var officer = await RecordTestData.CreateAccountAsync(db, "Officer");
        var records = RecordTestData.Records(db);
        await records.CreateAsync(subject.Id, officer.Id,
            RecordTestData.Write(title: "Sealed", affiliation: "Sith Order" + Sep + "Apprentice") with { IsSealed = true, OverrideCodeHash = "hash" }, Ct);
        await records.CreateAsync(subject.Id, subject.Id, RecordTestData.Write(title: "Open", affiliation: "Jedi Order"), Ct);

        var forSubject = await records.ListAffiliationsAsync(new RecordCaller(subject.Id), RecordListScope.Mine, null, null, Ct);
        var forOfficer = await records.ListAffiliationsAsync(new RecordCaller(officer.Id, true), RecordListScope.All, null, null, Ct);

        Assert.Equal(["Jedi Order"], forSubject.Primaries);
        Assert.Empty(forSubject.Secondaries);
        Assert.Equal(["Jedi Order", "Sith Order"], forOfficer.Primaries);
    }
}
