using System.Security.Cryptography;
using System.Text.Json;
using Gisr.Database.Common;
using Gisr.Database.Records;
using Gisr.Database.Tests.Common;
using Npgsql;

namespace Gisr.Database.Tests.Records;

[Collection("postgres")]
public class RecordRepositorySealingTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private const string Hash = "pbkdf2-sha256$1$c2FsdA==$aGFzaA==";

    private sealed record World(Guid Subject, Guid Officer, Guid Stranger, Guid SealedId, Guid OpenId);

    private static RecordWrite Sealed(string title = "Secret commendation", string affiliation = "military") =>
        RecordTestData.Write(title: title, affiliation: affiliation, incidents: [new IncidentWrite(new DateOnly(2020, 1, 1), "Hidden incident", "Hidden detail")]) with
        {
            IsSealed = true,
            OverrideCodeHash = Hash,
        };

    private async Task<(TestDatabase Db, World World)> SeedAsync()
    {
        var db = await postgres.CreateMigratedDatabaseAsync();
        var subject = await RecordTestData.CreateAccountAsync(db, "Citizen");
        var officer = await RecordTestData.CreateAccountAsync(db, "Officer");
        var stranger = await RecordTestData.CreateAccountAsync(db, "Stranger");
        var records = RecordTestData.Records(db);
        var sealedRecord = await records.CreateAsync(subject.Id, officer.Id, Sealed(), Ct);
        var open = await records.CreateAsync(subject.Id, subject.Id, RecordTestData.Write(title: "Open record"), Ct);
        return (db, new World(subject.Id, officer.Id, stranger.Id, sealedRecord.Record.Id, open.Record.Id));
    }

    [Fact]
    public async Task Create_StoresTheSealedFlagAndHash_ButRowsNeverCarryTheHash()
    {
        var (db, w) = await SeedAsync();
        await using var _ = db;

        var row = (await RecordTestData.Records(db).GetAsync(w.SealedId, new RecordCaller(w.Officer, true), Ct))!.Record;

        Assert.True(row.IsSealed);
        Assert.False(row.IsLocked);
        Assert.Equal(Hash, await db.ScalarAsync<string>($"SELECT override_code_hash FROM record WHERE id = '{w.SealedId}'"));
        Assert.DoesNotContain(typeof(RecordRow).GetProperties(), p => p.Name.Contains("Hash", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Create_ReturnsTheFullSealedRecord_ToItsAuthor()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var subject = await RecordTestData.CreateAccountAsync(db);
        var officer = await RecordTestData.CreateAccountAsync(db);

        var created = await RecordTestData.Records(db).CreateAsync(subject.Id, officer.Id, Sealed(), Ct);

        Assert.True(created.Record.IsSealed);
        Assert.False(created.Record.IsLocked);
        Assert.Equal("Secret commendation", created.Record.Title);
        Assert.Single(created.Incidents);
    }

    [Fact]
    public async Task Get_ForTheSubject_IsLockedAndRedacted_WithoutLoadingChildren()
    {
        var (db, w) = await SeedAsync();
        await using var _ = db;

        var locked = await RecordTestData.Records(db).GetAsync(w.SealedId, new RecordCaller(w.Subject), Ct);

        Assert.NotNull(locked);
        Assert.True(locked.Record.IsLocked);
        Assert.True(locked.Record.IsSealed);
        Assert.Equal(string.Empty, locked.Record.Title);
        Assert.Equal(string.Empty, locked.Record.Affiliation);
        Assert.Equal("{}", locked.Record.EngagementDetail);
        Assert.Null(locked.Record.EngagementStart);
        Assert.Null(locked.Record.EngagementEnd);
        Assert.Equal(string.Empty, locked.Record.SubjectDisplayName);
        Assert.Equal(string.Empty, locked.Record.AuthorDisplayName);
        Assert.Empty(locked.Incidents);
        Assert.Empty(locked.Awards);
        Assert.Equal(w.Officer, locked.Record.AuthorAccountId);
    }

    [Fact]
    public async Task Get_ForOfficers_IsNeverLocked_AndStrangersStillGetNothing()
    {
        var (db, w) = await SeedAsync();
        await using var _ = db;
        var records = RecordTestData.Records(db);

        var asOfficer = await records.GetAsync(w.SealedId, new RecordCaller(w.Officer, true), Ct);
        var asStranger = await records.GetAsync(w.SealedId, new RecordCaller(w.Stranger), Ct);

        Assert.False(asOfficer!.Record.IsLocked);
        Assert.Equal("Secret commendation", asOfficer.Record.Title);
        Assert.Equal("Hidden incident", Assert.Single(asOfficer.Incidents).Title);
        Assert.Null(asStranger);
    }

    [Fact]
    public async Task Get_ForTheSubjectWhoProvedTheCode_ReturnsTheFullRecord()
    {
        var (db, w) = await SeedAsync();
        await using var _ = db;

        var unlocked = await RecordTestData.Records(db).GetAsync(w.SealedId, new RecordCaller(w.Subject, false, true), Ct);

        Assert.False(unlocked!.Record.IsLocked);
        Assert.Equal("Secret commendation", unlocked.Record.Title);
        Assert.Equal("Citizen", unlocked.Record.SubjectDisplayName);
        Assert.Single(unlocked.Incidents);
    }

    [Fact]
    public async Task Get_UnlockedFlag_DoesNotWidenAccessToOtherPeoplesRecords()
    {
        var (db, w) = await SeedAsync();
        await using var _ = db;

        var viaStranger = await RecordTestData.Records(db).GetAsync(w.SealedId, new RecordCaller(w.Stranger, false, true), Ct);

        Assert.Null(viaStranger);
    }

    [Fact]
    public async Task UnsealedRecords_AreNeverRedacted()
    {
        var (db, w) = await SeedAsync();
        await using var _ = db;

        var open = await RecordTestData.Records(db).GetAsync(w.OpenId, new RecordCaller(w.Subject), Ct);

        Assert.False(open!.Record.IsLocked);
        Assert.False(open.Record.IsSealed);
        Assert.Equal("Open record", open.Record.Title);
    }

    [Fact]
    public async Task List_ForTheSubject_ShowsLockedRowsRedacted_AndCountsThem()
    {
        var (db, w) = await SeedAsync();
        await using var _ = db;

        var page = await RecordTestData.Records(db).ListAsync(new RecordCaller(w.Subject), RecordListScope.Mine, null, null, RecordSort.UpdatedAsc, 1, 20, Ct);

        Assert.Equal(2, page.TotalCount);
        var locked = page.Items.Single(r => r.IsLocked);
        Assert.Equal(w.SealedId, locked.Id);
        Assert.Equal(string.Empty, locked.Title);
        Assert.Equal(string.Empty, locked.Affiliation);
        Assert.Null(locked.EngagementStart);
        Assert.Equal(string.Empty, locked.AuthorDisplayName);
        Assert.Equal("Open record", page.Items.Single(r => !r.IsLocked).Title);
    }

    [Fact]
    public async Task List_ForOfficers_ShowsSealedRecordsInFull()
    {
        var (db, w) = await SeedAsync();
        await using var _ = db;

        var page = await RecordTestData.Records(db).ListAsync(
            new RecordCaller(w.Officer, true), RecordListScope.All, null, null, RecordSort.TitleAsc, 1, 20, Ct);

        var sealedRow = page.Items.Single(r => r.Id == w.SealedId);
        Assert.True(sealedRow.IsSealed);
        Assert.False(sealedRow.IsLocked);
        Assert.Equal("Secret commendation", sealedRow.Title);
    }

    [Fact]
    public async Task List_AffiliationFilter_DoesNotMatchLockedRows()
    {
        var (db, w) = await SeedAsync();
        await using var _ = db;
        var records = RecordTestData.Records(db);

        var military = await records.ListAsync(new RecordCaller(w.Subject), RecordListScope.Mine, null, "military", RecordSort.UpdatedDesc, 1, 20, Ct);

        Assert.Equal(["Open record"], military.Items.Select(r => r.Title));
        Assert.Equal(1, military.TotalCount);
    }

    [Fact]
    public async Task List_TitleSort_DoesNotRevealTheOrderOfLockedTitles()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var subject = await RecordTestData.CreateAccountAsync(db);
        var officer = await RecordTestData.CreateAccountAsync(db);
        var records = RecordTestData.Records(db);
        var bravo = await records.CreateAsync(subject.Id, officer.Id, Sealed("Bravo"), Ct);
        var alpha = await records.CreateAsync(subject.Id, officer.Id, Sealed("Alpha"), Ct);

        var ascending = await records.ListAsync(new RecordCaller(subject.Id), RecordListScope.Mine, null, null, RecordSort.TitleAsc, 1, 20, Ct);
        var descending = await records.ListAsync(new RecordCaller(subject.Id), RecordListScope.Mine, null, null, RecordSort.TitleDesc, 1, 20, Ct);

        Assert.Equal([bravo.Record.Id, alpha.Record.Id], ascending.Items.Select(r => r.Id));
        Assert.Equal([bravo.Record.Id, alpha.Record.Id], descending.Items.Select(r => r.Id));
    }

    [Fact]
    public async Task Update_ByAnOfficer_UnsealingClearsTheCode_AndEndsEveryViewingGrant()
    {
        var (db, w) = await SeedAsync();
        await using var _ = db;
        var grants = new ViewGrantRepository(db.DataSource, new SqlScriptLoader());
        var hash = SHA256.HashData([1, 2, 3]);
        await grants.InsertAsync(w.SealedId, w.Subject, hash, Ct);

        var result = await RecordTestData.Records(db).UpdateAsync(
            w.SealedId, new RecordCaller(w.Officer, true), 1, w.Officer, RecordTestData.Write(title: "Unsealed"), Ct);

        Assert.Equal(RecordUpdateOutcome.Updated, result.Outcome);
        Assert.False(result.Record!.Record.IsSealed);
        Assert.Null(await db.ScalarAsync<string>($"SELECT override_code_hash FROM record WHERE id = '{w.SealedId}'"));
        Assert.False(await grants.IsValidAsync(w.SealedId, w.Subject, hash, Ct));
    }

    [Fact]
    public async Task Update_ReplacingTheCode_EndsViewings_AndStoresTheNewHash()
    {
        var (db, w) = await SeedAsync();
        await using var _ = db;
        var grants = new ViewGrantRepository(db.DataSource, new SqlScriptLoader());
        var hash = SHA256.HashData([4, 5, 6]);
        await grants.InsertAsync(w.SealedId, w.Subject, hash, Ct);

        await RecordTestData.Records(db).UpdateAsync(
            w.SealedId, new RecordCaller(w.Officer, true), 1, w.Officer, Sealed("Secret commendation") with { OverrideCodeHash = "pbkdf2-sha256$1$bmV3$bmV3" }, Ct);

        Assert.Equal("pbkdf2-sha256$1$bmV3$bmV3", await db.ScalarAsync<string>($"SELECT override_code_hash FROM record WHERE id = '{w.SealedId}'"));
        Assert.False(await grants.IsValidAsync(w.SealedId, w.Subject, hash, Ct));
    }

    [Fact]
    public async Task Update_KeepingTheCode_LeavesTheHashAndViewingsAlone()
    {
        var (db, w) = await SeedAsync();
        await using var _ = db;
        var grants = new ViewGrantRepository(db.DataSource, new SqlScriptLoader());
        var hash = SHA256.HashData([7, 8, 9]);
        await grants.InsertAsync(w.SealedId, w.Subject, hash, Ct);

        var result = await RecordTestData.Records(db).UpdateAsync(
            w.SealedId, new RecordCaller(w.Officer, true), 1, w.Officer, Sealed("Edited title") with { OverrideCodeHash = null }, Ct);

        Assert.Equal(RecordUpdateOutcome.Updated, result.Outcome);
        Assert.True(result.Record!.Record.IsSealed);
        Assert.Equal(Hash, await db.ScalarAsync<string>($"SELECT override_code_hash FROM record WHERE id = '{w.SealedId}'"));
        Assert.True(await grants.IsValidAsync(w.SealedId, w.Subject, hash, Ct));
    }

    [Fact]
    public async Task Update_CanSealAnExistingServiceRecord()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var subject = await RecordTestData.CreateAccountAsync(db);
        var officer = await RecordTestData.CreateAccountAsync(db);
        var records = RecordTestData.Records(db);
        var created = await records.CreateAsync(subject.Id, officer.Id, RecordTestData.Write(), Ct);

        var result = await records.UpdateAsync(created.Record.Id, new RecordCaller(officer.Id, true), 1, officer.Id,
            RecordTestData.Write() with { IsSealed = true, OverrideCodeHash = Hash }, Ct);

        Assert.True(result.Record!.Record.IsSealed);
        Assert.True((await records.GetAsync(created.Record.Id, new RecordCaller(subject.Id), Ct))!.Record.IsLocked);
    }

    [Fact]
    public async Task Update_CannotSealARecordTheSubjectAuthored()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var subject = await RecordTestData.CreateAccountAsync(db);
        var records = RecordTestData.Records(db);
        var created = await records.CreateAsync(subject.Id, subject.Id, RecordTestData.Write(), Ct);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => records.UpdateAsync(
            created.Record.Id, new RecordCaller(subject.Id), 1, subject.Id, RecordTestData.Write() with { IsSealed = true, OverrideCodeHash = Hash }, Ct));

        Assert.Equal("23514", ex.SqlState);
    }

    [Fact]
    public async Task Snapshots_RecordSealedStateAndCodeExistence_NeverTheHash()
    {
        var (db, w) = await SeedAsync();
        await using var _ = db;

        var history = await RecordTestData.Revisions(db).ListAsync(w.SealedId, 1, 20, Ct);

        using var snapshot = JsonDocument.Parse(history.Items[0].Snapshot);
        Assert.True(snapshot.RootElement.GetProperty("isSealed").GetBoolean());
        Assert.True(snapshot.RootElement.GetProperty("hasOverrideCode").GetBoolean());
        Assert.DoesNotContain("pbkdf2", history.Items[0].Snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain(Hash, history.Items[0].Snapshot, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetOverrideCodeHash_IsOnlyAvailableForTheSubjectOfASealedRecord()
    {
        var (db, w) = await SeedAsync();
        await using var _ = db;
        var records = RecordTestData.Records(db);

        Assert.Equal(Hash, await records.GetOverrideCodeHashAsync(w.SealedId, w.Subject, Ct));
        Assert.Null(await records.GetOverrideCodeHashAsync(w.SealedId, w.Stranger, Ct));
        Assert.Null(await records.GetOverrideCodeHashAsync(w.OpenId, w.Subject, Ct));
        Assert.Null(await records.GetOverrideCodeHashAsync(Guid.NewGuid(), w.Subject, Ct));
    }
}
