using Gisr.Database.Tests.Common;
using Gisr.Database.Tests.Records;
using Npgsql;

namespace Gisr.Database.Tests.Artifacts;

[Collection("postgres")]
public class ArtifactMigrationTests(PostgresFixture postgres)
{
    private static async Task<string> SqlStateAsync(Task task)
    {
        var ex = await Assert.ThrowsAsync<PostgresException>(() => task);
        return ex.SqlState;
    }

    private static string Insert(Guid recordId, Guid accountId, long size = 10, bool isImage = false, string? description = null, string fileName = "a.txt") =>
        $"""
        INSERT INTO artifact (id, record_id, file_name, content_type, size_bytes, sha256, blob_name, is_image, description, uploaded_by_account_id, uploaded_at)
        VALUES ('{Guid.NewGuid()}', '{recordId}', '{fileName}', 'text/plain', {size}, 'abc', 'blob', {(isImage ? "true" : "false")}, {(description is null ? "NULL" : $"'{description}'")}, '{accountId}', now())
        """;

    private async Task<(TestDatabase Db, Guid RecordId, Guid AccountId)> SeedAsync()
    {
        var db = await postgres.CreateMigratedDatabaseAsync();
        var account = await RecordTestData.CreateAccountAsync(db);
        var created = await RecordTestData.Records(db).CreateAsync(account.Id, account.Id, RecordTestData.Write(), CancellationToken.None);
        return (db, created.Record.Id, account.Id);
    }

    [Fact]
    public async Task NewArtifacts_DefaultToPendingIndexing()
    {
        var (db, recordId, accountId) = await SeedAsync();
        await using var _ = db;

        await db.ExecuteAsync(Insert(recordId, accountId));

        Assert.Equal("pending", await db.ScalarAsync<string>("SELECT index_status FROM artifact"));
        Assert.Null(await db.ScalarAsync<string>("SELECT index_detail FROM artifact"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(26_214_401)]
    public async Task SizeOutsideTheAllowedRange_IsRejected(long size)
    {
        var (db, recordId, accountId) = await SeedAsync();
        await using var _ = db;

        Assert.Equal("23514", await SqlStateAsync(db.ExecuteAsync(Insert(recordId, accountId, size))));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(26_214_400)]
    public async Task SizeAtTheBounds_IsAccepted(long size)
    {
        var (db, recordId, accountId) = await SeedAsync();
        await using var _ = db;

        await db.ExecuteAsync(Insert(recordId, accountId, size));

        Assert.Equal(1L, await db.ScalarAsync<long>("SELECT count(*) FROM artifact"));
    }

    [Fact]
    public async Task Images_RequireADescription()
    {
        var (db, recordId, accountId) = await SeedAsync();
        await using var _ = db;

        Assert.Equal("23514", await SqlStateAsync(db.ExecuteAsync(Insert(recordId, accountId, isImage: true))));
        await db.ExecuteAsync(Insert(recordId, accountId, isImage: true, description: "A Jedi temple"));
        await db.ExecuteAsync(Insert(recordId, accountId));
    }

    [Fact]
    public async Task Descriptions_AreCappedAt500Characters()
    {
        var (db, recordId, accountId) = await SeedAsync();
        await using var _ = db;

        await db.ExecuteAsync(Insert(recordId, accountId, isImage: true, description: new string('d', 500)));
        Assert.Equal("23514", await SqlStateAsync(db.ExecuteAsync(Insert(recordId, accountId, isImage: true, description: new string('d', 501)))));
    }

    [Fact]
    public async Task FileNames_AreCappedAt255Characters()
    {
        var (db, recordId, accountId) = await SeedAsync();
        await using var _ = db;

        Assert.Equal("23514", await SqlStateAsync(db.ExecuteAsync(Insert(recordId, accountId, fileName: new string('f', 256)))));
    }

    [Fact]
    public async Task RemovedAtAndRemovedBy_AreSetTogether()
    {
        var (db, recordId, accountId) = await SeedAsync();
        await using var _ = db;
        await db.ExecuteAsync(Insert(recordId, accountId));

        Assert.Equal("23514", await SqlStateAsync(db.ExecuteAsync("UPDATE artifact SET removed_at = now()")));
        await db.ExecuteAsync($"UPDATE artifact SET removed_at = now(), removed_by_account_id = '{accountId}'");
    }

    [Fact]
    public async Task IndexStatusAndDetail_AreRestrictedToKnownValues()
    {
        var (db, recordId, accountId) = await SeedAsync();
        await using var _ = db;
        await db.ExecuteAsync(Insert(recordId, accountId));

        Assert.Equal("23514", await SqlStateAsync(db.ExecuteAsync("UPDATE artifact SET index_status = 'done'")));
        Assert.Equal("23514", await SqlStateAsync(db.ExecuteAsync("UPDATE artifact SET index_detail = 'the file text'")));
        await db.ExecuteAsync("UPDATE artifact SET index_status = 'skipped', index_detail = 'no_text'");
    }

    [Fact]
    public async Task AnArtifact_MustBelongToARealRecord()
    {
        var (db, _, accountId) = await SeedAsync();
        await using var _ = db;

        Assert.Equal("23503", await SqlStateAsync(db.ExecuteAsync(Insert(Guid.NewGuid(), accountId))));
    }
}
