using Gisr.Database.Artifacts;
using Gisr.Database.Common;
using Gisr.Database.Records;
using Gisr.Database.Search;
using Gisr.Database.Tests.Artifacts;
using Gisr.Database.Tests.Common;
using Gisr.Database.Tests.Records;

namespace Gisr.Database.Tests.Search;

[Collection("postgres")]
public class SearchRepositoryTests(PostgresFixture postgres)
{
    private const double MaxDistance = 0.7;

    private static SearchRepository Search(TestDatabase db) => new(db.DataSource, new SqlScriptLoader());

    // Unit vectors in the first two dimensions: cosine distance to Query is 1 - cos(angle).
    private static float[] Vector(double angleDegrees)
    {
        var vector = new float[SearchRepository.Dimensions];
        var radians = angleDegrees * Math.PI / 180;
        vector[0] = (float)Math.Cos(radians);
        vector[1] = (float)Math.Sin(radians);
        return vector;
    }

    private static readonly float[] Query = Vector(0);

    private static ChunkWrite Chunk(double angle, string content = "chunk text", int index = 0) => new(index, content, Vector(angle));

    private static Task<PagedRows<SearchHitRow>> FindAsync(
        TestDatabase db, RecordCaller caller, RecordListScope scope = RecordListScope.Mine, string? affiliation = null, int page = 1, int pageSize = 10, double maxDistance = MaxDistance) =>
        Search(db).SearchAsync(caller, scope, Query, affiliation, maxDistance, page, pageSize, CancellationToken.None);

    private static async Task<Guid> IndexedRecordAsync(
        TestDatabase db, Gisr.Database.Accounts.AccountRow subject, double angle, string title = "Clone Wars service", string affiliation = "military", Gisr.Database.Accounts.AccountRow? author = null, bool sealedRecord = false)
    {
        var write = RecordTestData.Write(title: title, affiliation: affiliation, detail: affiliation == "military" ? """{"summary":"Served","rank":"Captain"}""" : """{"summary":"Served"}""");
        if (sealedRecord)
        {
            write = write with { IsSealed = true, OverrideCodeHash = "pbkdf2-sha256$1$salt$hash" };
        }

        var created = await RecordTestData.Records(db).CreateAsync(subject.Id, (author ?? subject).Id, write, CancellationToken.None);
        await Search(db).ReplaceChunksAsync(created.Record.Id, null, [Chunk(angle, $"{title} text")], CancellationToken.None);
        return created.Record.Id;
    }

    [Fact]
    public async Task ASubject_FindsTheirOwnRecords_OrderedByDistance_WithScoreInputs()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var account = await RecordTestData.CreateAccountAsync(db, "Rex");
        var far = await IndexedRecordAsync(db, account, 40, "Far");
        var near = await IndexedRecordAsync(db, account, 5, "Near");
        await IndexedRecordAsync(db, account, 80, "Unrelated");

        var result = await FindAsync(db, new RecordCaller(account.Id));

        Assert.Equal(2, result.TotalCount);
        Assert.Equal([near, far], result.Items.Select(h => h.RecordId));
        var hit = result.Items[0];
        Assert.Equal("Near", hit.RecordTitle);
        Assert.Equal("military", hit.Affiliation);
        Assert.Equal("Rex", hit.SubjectDisplayName);
        Assert.Equal("record", hit.SourceType);
        Assert.Null(hit.ArtifactId);
        Assert.Null(hit.ArtifactFileName);
        Assert.False(hit.IsSealed);
        Assert.Equal("Near text", hit.Content);
        Assert.InRange(hit.Distance, 1 - Math.Cos(5 * Math.PI / 180) - 1e-4, 1 - Math.Cos(5 * Math.PI / 180) + 1e-4);
    }

    [Fact]
    public async Task AnotherUsersChunks_AreNeverReturnedOrCounted()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var mine = await RecordTestData.CreateAccountAsync(db);
        var other = await RecordTestData.CreateAccountAsync(db);
        var own = await IndexedRecordAsync(db, mine, 5, "Mine");
        await IndexedRecordAsync(db, other, 0, "Theirs");

        var result = await FindAsync(db, new RecordCaller(mine.Id));
        var scopeAll = await FindAsync(db, new RecordCaller(mine.Id), RecordListScope.All);

        Assert.Equal([own], result.Items.Select(h => h.RecordId));
        Assert.Equal(1, result.TotalCount);
        Assert.Empty(scopeAll.Items);
        Assert.Equal(0, scopeAll.TotalCount);
    }

    [Fact]
    public async Task AnOfficer_SeesEveryonesRecords_IncludingSealed_WithScopeAll()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var citizen = await RecordTestData.CreateAccountAsync(db, "Citizen");
        var officer = await RecordTestData.CreateAccountAsync(db, "Officer");
        var open = await IndexedRecordAsync(db, citizen, 5, "Open");
        var secret = await IndexedRecordAsync(db, citizen, 10, "Secret", author: officer, sealedRecord: true);

        var result = await FindAsync(db, new RecordCaller(officer.Id, true), RecordListScope.All);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal([open, secret], result.Items.Select(h => h.RecordId));
        Assert.False(result.Items[0].IsSealed);
        Assert.True(result.Items[1].IsSealed);
    }

    [Fact]
    public async Task ALockedSealedRecord_NeverMatches_ForItsSubject()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var citizen = await RecordTestData.CreateAccountAsync(db);
        var officer = await RecordTestData.CreateAccountAsync(db);
        var open = await IndexedRecordAsync(db, citizen, 5, "Open");
        var secret = await IndexedRecordAsync(db, citizen, 0, "Secret", author: officer, sealedRecord: true);
        var artifact = await ArtifactTestData.AddAsync(db, secret, officer.Id, "secret.pdf");
        await Search(db).ReplaceChunksAsync(secret, artifact.Id, [Chunk(0, "secret file text")], CancellationToken.None);

        var result = await FindAsync(db, new RecordCaller(citizen.Id));

        Assert.Equal([open], result.Items.Select(h => h.RecordId));
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task AnOfficers_MineScope_CoversTheirOwnRecordsOnly()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var citizen = await RecordTestData.CreateAccountAsync(db);
        var officer = await RecordTestData.CreateAccountAsync(db);
        await IndexedRecordAsync(db, citizen, 5, "Citizen record");
        var own = await IndexedRecordAsync(db, officer, 5, "Officer record");

        var result = await FindAsync(db, new RecordCaller(officer.Id, true), RecordListScope.Mine);

        Assert.Equal([own], result.Items.Select(h => h.RecordId));
    }

    [Fact]
    public async Task TheAffiliationFilter_NarrowsTheResults()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var account = await RecordTestData.CreateAccountAsync(db);
        await IndexedRecordAsync(db, account, 5, "Soldier");
        var jedi = await IndexedRecordAsync(db, account, 8, "Knight", affiliation: "jedi");

        var result = await FindAsync(db, new RecordCaller(account.Id), affiliation: "jedi");

        Assert.Equal([jedi], result.Items.Select(h => h.RecordId));
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task OnlyChunksCloserThanTheCutOff_Match()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var account = await RecordTestData.CreateAccountAsync(db);
        var close = await IndexedRecordAsync(db, account, 30, "Close");
        await IndexedRecordAsync(db, account, 60, "Distant");

        var strict = await FindAsync(db, new RecordCaller(account.Id), maxDistance: 0.2);
        var lenient = await FindAsync(db, new RecordCaller(account.Id), maxDistance: 0.6);

        Assert.Equal([close], strict.Items.Select(h => h.RecordId));
        Assert.Equal(2, lenient.TotalCount);
    }

    [Fact]
    public async Task EachSource_ContributesItsBestChunkOnly()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var account = await RecordTestData.CreateAccountAsync(db);
        var recordId = await IndexedRecordAsync(db, account, 30, "Album");
        await Search(db).ReplaceChunksAsync(recordId, null, [Chunk(30, "weaker", 0), Chunk(10, "best", 1), Chunk(20, "middle", 2)], CancellationToken.None);
        var artifact = await ArtifactTestData.AddAsync(db, recordId, account.Id, "orders.pdf");
        await Search(db).ReplaceChunksAsync(recordId, artifact.Id, [Chunk(15, "file one", 0), Chunk(12, "file best", 1)], CancellationToken.None);

        var result = await FindAsync(db, new RecordCaller(account.Id));

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(["best", "file best"], result.Items.Select(h => h.Content));
        var fileHit = result.Items.Single(h => h.SourceType == "artifact");
        Assert.Equal(artifact.Id, fileHit.ArtifactId);
        Assert.Equal("orders.pdf", fileHit.ArtifactFileName);
        Assert.Equal("Album", fileHit.RecordTitle);
    }

    [Fact]
    public async Task RemovedArtifacts_AreExcluded_EvenIfTheirChunksRemain()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var account = await RecordTestData.CreateAccountAsync(db);
        var recordId = await IndexedRecordAsync(db, account, 80, "Album");
        var artifact = await ArtifactTestData.AddAsync(db, recordId, account.Id, "orders.pdf");
        await Search(db).ReplaceChunksAsync(recordId, artifact.Id, [Chunk(0, "file text")], CancellationToken.None);
        Assert.Equal(1, (await FindAsync(db, new RecordCaller(account.Id))).TotalCount);

        await db.ExecuteAsync($"UPDATE artifact SET removed_at = now(), removed_by_account_id = '{account.Id}' WHERE id = '{artifact.Id}'");

        Assert.Equal(0, (await FindAsync(db, new RecordCaller(account.Id))).TotalCount);
    }

    [Fact]
    public async Task RemovingAnArtifact_DeletesItsChunks()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var account = await RecordTestData.CreateAccountAsync(db);
        var recordId = await IndexedRecordAsync(db, account, 70, "Album");
        var artifact = await ArtifactTestData.AddAsync(db, recordId, account.Id, "orders.pdf");
        await Search(db).ReplaceChunksAsync(recordId, artifact.Id, [Chunk(0, "file text")], CancellationToken.None);

        await ArtifactTestData.Artifacts(db).RemoveAsync(artifact.Id, account.Id, CancellationToken.None);

        Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT count(*) FROM search_chunk WHERE artifact_id = '{artifact.Id}'"));
        Assert.Equal(1L, await db.ScalarAsync<long>($"SELECT count(*) FROM search_chunk WHERE record_id = '{recordId}'"));
    }

    [Fact]
    public async Task Paging_SlicesTheOrderedResults_AndKeepsTheTotal()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var account = await RecordTestData.CreateAccountAsync(db);
        var ids = new List<Guid>();
        foreach (var angle in new[] { 5, 10, 15, 20, 25 })
        {
            ids.Add(await IndexedRecordAsync(db, account, angle, $"Record {angle}"));
        }

        var page1 = await FindAsync(db, new RecordCaller(account.Id), page: 1, pageSize: 2);
        var page3 = await FindAsync(db, new RecordCaller(account.Id), page: 3, pageSize: 2);
        var beyond = await FindAsync(db, new RecordCaller(account.Id), page: 9, pageSize: 2);

        Assert.Equal(5, page1.TotalCount);
        Assert.Equal(ids.Take(2), page1.Items.Select(h => h.RecordId));
        Assert.Equal([ids[4]], page3.Items.Select(h => h.RecordId));
        Assert.Empty(beyond.Items);
        Assert.Equal(5, beyond.TotalCount);
    }

    [Fact]
    public async Task ReplacingChunks_SwapsThePreviousOnesForTheNewSet()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var account = await RecordTestData.CreateAccountAsync(db);
        var recordId = await IndexedRecordAsync(db, account, 5, "Album");
        var search = Search(db);

        await search.ReplaceChunksAsync(recordId, null, [Chunk(5, "a", 0), Chunk(6, "b", 1)], CancellationToken.None);
        Assert.Equal(2L, await db.ScalarAsync<long>($"SELECT count(*) FROM search_chunk WHERE record_id = '{recordId}'"));

        await search.ReplaceChunksAsync(recordId, null, [Chunk(5, "only", 0)], CancellationToken.None);
        Assert.Equal(1L, await db.ScalarAsync<long>($"SELECT count(*) FROM search_chunk WHERE record_id = '{recordId}'"));

        await search.ReplaceChunksAsync(recordId, null, [], CancellationToken.None);
        Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT count(*) FROM search_chunk WHERE record_id = '{recordId}'"));
    }

    [Fact]
    public async Task ReplacingTheRecordChunks_LeavesTheArtifactChunksAlone()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var account = await RecordTestData.CreateAccountAsync(db);
        var recordId = await IndexedRecordAsync(db, account, 5, "Album");
        var artifact = await ArtifactTestData.AddAsync(db, recordId, account.Id, "orders.pdf");
        var search = Search(db);
        await search.ReplaceChunksAsync(recordId, artifact.Id, [Chunk(5, "file text")], CancellationToken.None);

        await search.ReplaceChunksAsync(recordId, null, [Chunk(5, "new record text")], CancellationToken.None);

        Assert.Equal(1L, await db.ScalarAsync<long>($"SELECT count(*) FROM search_chunk WHERE artifact_id = '{artifact.Id}'"));
        Assert.Equal(1L, await db.ScalarAsync<long>($"SELECT count(*) FROM search_chunk WHERE record_id = '{recordId}' AND artifact_id IS NULL"));
    }
}
