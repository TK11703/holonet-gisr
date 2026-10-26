using Gisr.Database.Tests.Common;
using Gisr.Database.Tests.Records;
using Npgsql;

namespace Gisr.Database.Tests.Search;

[Collection("postgres")]
public class SearchMigrationTests(PostgresFixture postgres)
{
    private static async Task<string> SqlStateAsync(Task task)
    {
        var ex = await Assert.ThrowsAsync<PostgresException>(() => task);
        return ex.SqlState;
    }

    private static string Vector(int dimensions) => "[" + string.Join(",", Enumerable.Repeat("0.1", dimensions)) + "]";

    private static string InsertChunk(Guid recordId, string source = "record", Guid? artifactId = null, int dimensions = 1536) =>
        $"""
        INSERT INTO search_chunk (id, record_id, source_type, artifact_id, chunk_index, content, embedding, created_at)
        VALUES ('{Guid.NewGuid()}', '{recordId}', '{source}', {(artifactId is null ? "NULL" : $"'{artifactId}'")}, 0, 'text', '{Vector(dimensions)}', now())
        """;

    private async Task<(TestDatabase Db, Guid RecordId)> SeedAsync()
    {
        var db = await postgres.CreateMigratedDatabaseAsync();
        var account = await RecordTestData.CreateAccountAsync(db);
        var record = await RecordTestData.Records(db).CreateAsync(account.Id, account.Id, RecordTestData.Write(), CancellationToken.None);
        return (db, record.Record.Id);
    }

    [Fact]
    public async Task TheEmbeddingColumn_Is1536DimensionalVector()
    {
        var (db, _) = await SeedAsync();
        await using var _ = db;

        var type = await db.ScalarAsync<string>(
            "SELECT format_type(atttypid, atttypmod) FROM pg_attribute WHERE attrelid = 'search_chunk'::regclass AND attname = 'embedding'");

        Assert.Equal("vector(1536)", type);
    }

    [Fact]
    public async Task AnEmbeddingOfTheWrongSize_IsRejected()
    {
        var (db, recordId) = await SeedAsync();
        await using var _ = db;

        await db.ExecuteAsync(InsertChunk(recordId));
        Assert.Equal("22000", await SqlStateAsync(db.ExecuteAsync(InsertChunk(recordId, dimensions: 3))));
    }

    [Fact]
    public async Task ThereIsAnHnswCosineIndex_OnTheEmbeddings()
    {
        var (db, _) = await SeedAsync();
        await using var _ = db;

        var definition = await db.ScalarAsync<string>(
            "SELECT indexdef FROM pg_indexes WHERE tablename = 'search_chunk' AND indexdef ILIKE '%hnsw%'");

        Assert.NotNull(definition);
        Assert.Contains("vector_cosine_ops", definition, StringComparison.Ordinal);
        Assert.Contains("embedding", definition, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChunkSourceAndArtifactId_MustAgree()
    {
        var (db, recordId) = await SeedAsync();
        await using var _ = db;

        Assert.Equal("23514", await SqlStateAsync(db.ExecuteAsync(InsertChunk(recordId, "artifact"))));
        Assert.Equal("23514", await SqlStateAsync(db.ExecuteAsync(InsertChunk(recordId, "record", Guid.NewGuid()))));
        Assert.Equal("23514", await SqlStateAsync(db.ExecuteAsync(InsertChunk(recordId, "other"))));
    }

    [Fact]
    public async Task AChunk_MustBelongToARealRecord()
    {
        var (db, _) = await SeedAsync();
        await using var _ = db;

        Assert.Equal("23503", await SqlStateAsync(db.ExecuteAsync(InsertChunk(Guid.NewGuid()))));
    }

    [Fact]
    public async Task IndexJobs_RestrictKindAndStatus_AndRequireAnArtifactIdForArtifacts()
    {
        var (db, recordId) = await SeedAsync();
        await using var _ = db;
        string Job(string kind, string status, string? artifact) =>
            $"INSERT INTO search_index_job (id, kind, record_id, artifact_id, status, available_at, created_at, updated_at) VALUES ('{Guid.NewGuid()}', '{kind}', '{recordId}', {(artifact is null ? "NULL" : $"'{artifact}'")}, '{status}', now(), now(), now())";

        await db.ExecuteAsync(Job("record", "pending", null));
        await db.ExecuteAsync(Job("artifact", "pending", Guid.NewGuid().ToString()));

        Assert.Equal("23514", await SqlStateAsync(db.ExecuteAsync(Job("record", "done", null))));
        Assert.Equal("23514", await SqlStateAsync(db.ExecuteAsync(Job("other", "pending", null))));
        Assert.Equal("23514", await SqlStateAsync(db.ExecuteAsync(Job("artifact", "pending", null))));
        Assert.Equal("23514", await SqlStateAsync(db.ExecuteAsync(Job("record", "pending", Guid.NewGuid().ToString()))));
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT attempts FROM search_index_job LIMIT 1"));
    }

    [Fact]
    public async Task OcrUsage_IsKeyedByTheFirstDayOfTheMonth()
    {
        var (db, _) = await SeedAsync();
        await using var _ = db;

        await db.ExecuteAsync("INSERT INTO ocr_usage (month, pages_used) VALUES ('2026-10-01', 5)");

        Assert.Equal("23505", await SqlStateAsync(db.ExecuteAsync("INSERT INTO ocr_usage (month, pages_used) VALUES ('2026-10-01', 1)")));
        Assert.Equal("23514", await SqlStateAsync(db.ExecuteAsync("INSERT INTO ocr_usage (month, pages_used) VALUES ('2026-11-15', 1)")));
        Assert.Equal("23514", await SqlStateAsync(db.ExecuteAsync("INSERT INTO ocr_usage (month, pages_used) VALUES ('2026-12-01', -1)")));
        Assert.Equal("ocr_usage_pkey", await db.ScalarAsync<string>("SELECT conname FROM pg_constraint WHERE conrelid = 'ocr_usage'::regclass AND contype = 'p'"));
    }
}
