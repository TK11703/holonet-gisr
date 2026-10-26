using Gisr.Database.Migrations;
using Gisr.Database.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Gisr.Database.Tests.Common;

namespace Gisr.Database.Tests.Migrations;

[Collection("postgres")]
public class MigrationRunnerTests(PostgresFixture postgres)
{
    private sealed class FakeSource(params MigrationScript[] scripts) : IMigrationSource
    {
        public IReadOnlyList<MigrationScript> GetScripts() => scripts;
    }

    private static MigrationRunner CreateRunner(TestDatabase db, params MigrationScript[] scripts) =>
        new(db.DataSource, new FakeSource(scripts), new SqlScriptLoader(), NullLogger<MigrationRunner>.Instance);

    [Fact]
    public async Task Apply_RunsScriptsInOrderAndRecordsLedger()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var first = new MigrationScript("0001_a", "CREATE TABLE a (id int PRIMARY KEY);");
        var second = new MigrationScript("0002_b", "CREATE TABLE b (id int PRIMARY KEY);");

        await CreateRunner(db, second, first).ApplyAsync(CancellationToken.None);

        Assert.Equal(2, await db.ScalarAsync<int>("SELECT count(*) FROM schema_migration"));
        Assert.Equal("0001_a", await db.ScalarAsync<string>("SELECT script_id FROM schema_migration ORDER BY applied_at, script_id LIMIT 1"));
        Assert.Equal(MigrationRunner.ComputeChecksum(first.Sql),
            await db.ScalarAsync<string>("SELECT checksum_sha256 FROM schema_migration WHERE script_id = '0001_a'"));
        Assert.Equal(1, await db.ScalarAsync<int>("SELECT count(*) FROM information_schema.tables WHERE table_name = 'b'"));
    }

    [Fact]
    public async Task Apply_SecondRunIsNoOp()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var script = new MigrationScript("0001_a", "CREATE TABLE a (id int PRIMARY KEY);");
        var runner = CreateRunner(db, script);

        await runner.ApplyAsync(CancellationToken.None);
        var appliedAt = await db.ScalarAsync<DateTime>("SELECT applied_at FROM schema_migration");
        await runner.ApplyAsync(CancellationToken.None);

        Assert.Equal(1, await db.ScalarAsync<int>("SELECT count(*) FROM schema_migration"));
        Assert.Equal(appliedAt, await db.ScalarAsync<DateTime>("SELECT applied_at FROM schema_migration"));
    }

    [Fact]
    public async Task Apply_Fails_WhenAppliedScriptChecksumChanged()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        await CreateRunner(db, new MigrationScript("0001_a", "CREATE TABLE a (id int PRIMARY KEY);"))
            .ApplyAsync(CancellationToken.None);

        var tampered = CreateRunner(db, new MigrationScript("0001_a", "CREATE TABLE a (id int PRIMARY KEY, extra int);"));

        var ex = await Assert.ThrowsAsync<MigrationChecksumException>(() => tampered.ApplyAsync(CancellationToken.None));
        Assert.Equal("0001_a", ex.ScriptId);
    }

    [Fact]
    public async Task Apply_RollsBackFailingScriptAndDoesNotRecordIt()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var failing = new MigrationScript("0001_bad", "CREATE TABLE t1 (id int); SELECT 1/0;");

        await Assert.ThrowsAnyAsync<Exception>(() => CreateRunner(db, failing).ApplyAsync(CancellationToken.None));

        Assert.Equal(0, await db.ScalarAsync<int>("SELECT count(*) FROM schema_migration"));
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT count(*) FROM information_schema.tables WHERE table_name = 't1'"));
    }

    [Fact]
    public async Task Apply_ConcurrentRunnersApplyEachScriptOnce()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var script = new MigrationScript("0001_seed", "CREATE TABLE seed (id int); INSERT INTO seed VALUES (1);");

        await Task.WhenAll(
            CreateRunner(db, script).ApplyAsync(CancellationToken.None),
            CreateRunner(db, script).ApplyAsync(CancellationToken.None));

        Assert.Equal(1, await db.ScalarAsync<int>("SELECT count(*) FROM schema_migration"));
        Assert.Equal(1, await db.ScalarAsync<int>("SELECT count(*) FROM seed"));
    }

    [Fact]
    public async Task Apply_EmbeddedMigrationsApplyToEmptyDatabase()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var runner = new MigrationRunner(db.DataSource, new EmbeddedMigrationSource(), new SqlScriptLoader(), NullLogger<MigrationRunner>.Instance);

        await runner.ApplyAsync(CancellationToken.None);

        Assert.Equal(1, await db.ScalarAsync<int>("SELECT count(*) FROM pg_extension WHERE extname = 'vector'"));
    }
}
