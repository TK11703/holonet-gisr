using Gisr.Database.Common;
using Gisr.Database.Gallery;
using Gisr.Database.Records;
using Gisr.Database.Search;
using Gisr.Database.Tests.Common;

namespace Gisr.Database.Tests.Common;

// Slow: runs only when GISR_RUN_SCALE_TESTS=1 (always set for the pre-merge gates).
public sealed class ScaleFactAttribute : FactAttribute
{
    public const string EnvironmentVariable = "GISR_RUN_SCALE_TESTS";

    public ScaleFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(EnvironmentVariable) != "1")
        {
            Skip = $"Scale test; set {EnvironmentVariable}=1 to run it.";
        }
    }
}

[Collection("postgres")]
[Trait("Category", "Scale")]
public class ScaleSmokeTests(PostgresFixture postgres)
{
    private const int Records = 10_000;
    private const int Accounts = 100;
    private const int ArtifactsPerRecord = 5;
    private const int ImagesPerRecord = 2;
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(2);

    // Set-based so 10,000 records, 50,000 artifacts and 40,000 chunks load in seconds; the HNSW index is built after the load.
    private const string SeedSql = """
        DROP INDEX ix_search_chunk_embedding;

        CREATE TABLE scale_acc AS
            SELECT n, gen_random_uuid() AS id FROM generate_series(1, 100) n;
        INSERT INTO account (id, tenant_id, entra_object_id, display_name, created_at, last_sign_in_at)
            SELECT id, '11111111-1111-1111-1111-111111111111', gen_random_uuid(), 'User ' || n, now(), now() FROM scale_acc;

        CREATE TABLE scale_rec AS
            SELECT n, gen_random_uuid() AS id, (n % 20 = 0) AS is_sealed FROM generate_series(1, 10000) n;
        INSERT INTO record (id, subject_account_id, author_account_id, title, affiliation, engagement_detail,
                            is_sealed, override_code_hash, created_at, updated_at, updated_by_account_id)
            SELECT r.id, s.id, CASE WHEN r.is_sealed THEN a.id ELSE s.id END, 'Record ' || r.n,
                   (ARRAY['military', 'jedi', 'bounty_hunter'])[1 + r.n % 3], '{"summary":"Synthetic record"}'::jsonb,
                   r.is_sealed, CASE WHEN r.is_sealed THEN 'pbkdf2-sha256$1$salt$hash' END,
                   now() - (r.n || ' minutes')::interval, now() - (r.n || ' minutes')::interval, s.id
            FROM scale_rec r
            JOIN scale_acc s ON s.n = 1 + r.n % 100
            JOIN scale_acc a ON a.n = 1 + (r.n + 1) % 100;

        CREATE TABLE scale_art AS
            SELECT gen_random_uuid() AS id, r.id AS record_id, k, (k <= 2) AS is_image
            FROM scale_rec r CROSS JOIN generate_series(1, 5) k;
        INSERT INTO artifact (id, record_id, file_name, content_type, size_bytes, sha256, blob_name, thumbnail_blob_name,
                              is_image, description, uploaded_by_account_id, uploaded_at, index_status, index_detail)
            SELECT x.id, x.record_id, 'file' || x.k || CASE WHEN x.is_image THEN '.png' ELSE '.pdf' END,
                   CASE WHEN x.is_image THEN 'image/png' ELSE 'application/pdf' END, 1000, repeat('a', 64),
                   'blob/' || x.id, CASE WHEN x.is_image THEN 'thumb/' || x.id END, x.is_image,
                   CASE WHEN x.is_image THEN 'Synthetic image' END, r.author_account_id, now(),
                   CASE WHEN x.is_image THEN 'skipped' ELSE 'indexed' END,
                   CASE WHEN x.is_image THEN 'unsupported_type' END
            FROM scale_art x JOIN record r ON r.id = x.record_id;

        CREATE TABLE scale_pool AS
            SELECT g AS id,
                   ('[' || (SELECT string_agg((random() - 0.5)::text, ',') FROM generate_series(1, 1536) d WHERE g IS NOT NULL) || ']')::vector(1536) AS v
            FROM generate_series(1, 2000) g;

        INSERT INTO search_chunk (id, record_id, source_type, artifact_id, chunk_index, content, embedding, created_at)
            SELECT gen_random_uuid(), r.id, 'record', NULL, 0, 'Synthetic record text ' || r.n, p.v, now()
            FROM scale_rec r JOIN scale_pool p ON p.id = 1 + r.n % 2000;
        INSERT INTO search_chunk (id, record_id, source_type, artifact_id, chunk_index, content, embedding, created_at)
            SELECT gen_random_uuid(), x.record_id, 'artifact', x.id, 0, 'Synthetic file text', p.v, now()
            FROM scale_art x JOIN scale_pool p ON p.id = 1 + abs(hashtext(x.id::text)) % 2000
            WHERE NOT x.is_image;

        CREATE INDEX ix_search_chunk_embedding ON search_chunk USING hnsw (embedding vector_cosine_ops);
        ANALYZE;
        """;

    private static async Task<float[]> PoolVectorAsync(TestDatabase db)
    {
        await using var command = db.DataSource.CreateCommand("SELECT v::real[] FROM scale_pool WHERE id = 2");
        return (float[])(await command.ExecuteScalarAsync())!;
    }

    private static async Task<TimeSpan> TimeAsync(string name, List<string> report, Func<Task> action)
    {
        await action();
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        await action();
        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started);
        report.Add($"{name}: {elapsed.TotalMilliseconds:0} ms");
        return elapsed;
    }

    [ScaleFact]
    public async Task SearchGalleryAndRecordList_StayUnderTwoSeconds_At10kRecordsAnd50kArtifacts()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        await using (var seed = db.DataSource.CreateCommand(SeedSql))
        {
            seed.CommandTimeout = 900;
            await seed.ExecuteNonQueryAsync();
        }
        Assert.Equal(Records, await db.ScalarAsync<int>("SELECT count(*) FROM record"));
        Assert.Equal(Records * ArtifactsPerRecord, await db.ScalarAsync<int>("SELECT count(*) FROM artifact"));
        Assert.Equal(Records * (ArtifactsPerRecord - ImagesPerRecord) + Records, await db.ScalarAsync<int>("SELECT count(*) FROM search_chunk"));

        var sql = new SqlScriptLoader();
        // Account 2 owns records 1, 101, 201... (never sealed); record 1 and every 2000th after it share the query's vector.
        var accountId = await db.ScalarAsync<Guid>("SELECT id FROM scale_acc WHERE n = 2");
        var officer = new RecordCaller(await db.ScalarAsync<Guid>("SELECT id FROM scale_acc WHERE n = 1"), true);
        var citizen = new RecordCaller(accountId);
        var query = await PoolVectorAsync(db);
        var search = new SearchRepository(db.DataSource, sql);
        var gallery = new GalleryRepository(db.DataSource, sql);
        var records = new RecordRepository(db.DataSource, sql);
        var report = new List<string>();
        var slow = new List<string>();

        async Task CheckAsync(string name, Func<Task> action)
        {
            var elapsed = await TimeAsync(name, report, action);
            if (elapsed > Limit)
            {
                slow.Add($"{name} took {elapsed.TotalMilliseconds:0} ms");
            }
        }

        await CheckAsync("search (Officer, all)", async () =>
            Assert.NotEmpty((await search.SearchAsync(officer, RecordListScope.All, query, null, 0.7, 1, 10, CancellationToken.None)).Items));
        await CheckAsync("search (citizen)", async () =>
            Assert.NotEmpty((await search.SearchAsync(citizen, RecordListScope.Mine, query, null, 0.7, 1, 10, CancellationToken.None)).Items));
        await CheckAsync("search (Officer, affiliation filter, page 3)", async () =>
            await search.SearchAsync(officer, RecordListScope.All, query, "jedi", 0.7, 3, 10, CancellationToken.None));
        await CheckAsync("gallery (Officer, all)", async () =>
            Assert.NotEmpty((await gallery.ListGroupsAsync(officer, RecordListScope.All, 1, 12, CancellationToken.None)).Items));
        await CheckAsync("gallery (citizen)", async () =>
            Assert.NotEmpty((await gallery.ListGroupsAsync(citizen, RecordListScope.Mine, 1, 12, CancellationToken.None)).Items));
        await CheckAsync("record list (Officer, all)", async () =>
            Assert.Equal(20, (await records.ListAsync(officer, RecordListScope.All, null, null, RecordSort.UpdatedDesc, 1, 20, CancellationToken.None)).Items.Count));
        await CheckAsync("record list (Officer, all, last page)", async () =>
            Assert.NotEmpty((await records.ListAsync(officer, RecordListScope.All, null, null, RecordSort.UpdatedDesc, 500, 20, CancellationToken.None)).Items));
        await CheckAsync("record list (citizen)", async () =>
            Assert.NotEmpty((await records.ListAsync(citizen, RecordListScope.Mine, null, null, RecordSort.UpdatedDesc, 1, 20, CancellationToken.None)).Items));

        Assert.True(slow.Count == 0, Environment.NewLine + string.Join(Environment.NewLine, slow) + Environment.NewLine + "All: " + string.Join("; ", report));
    }
}
