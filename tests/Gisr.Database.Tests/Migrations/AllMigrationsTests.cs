using Gisr.Database.Common;
using Gisr.Database.Migrations;
using Gisr.Database.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gisr.Database.Tests.Migrations;

[Collection("postgres")]
public class AllMigrationsTests(PostgresFixture postgres)
{
    private static MigrationRunner CreateRunner(TestDatabase db) =>
        new(db.DataSource, new EmbeddedMigrationSource(), new SqlScriptLoader(), NullLogger<MigrationRunner>.Instance);

    [Fact]
    public async Task EveryEmbeddedMigration_AppliesToAnEmptyDatabase_AndIsRecordedWithItsChecksum()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        var scripts = new EmbeddedMigrationSource().GetScripts();

        await CreateRunner(db).ApplyAsync(CancellationToken.None);

        Assert.Equal(15, scripts.Count);
        Assert.Equal(scripts.Count, await db.ScalarAsync<int>("SELECT count(*) FROM schema_migration"));
        foreach (var script in scripts)
        {
            Assert.Equal(
                MigrationRunner.ComputeChecksum(script.Sql),
                await db.ScalarAsync<string>($"SELECT checksum_sha256 FROM schema_migration WHERE script_id = '{script.Id}'"));
        }
    }

    [Fact]
    public void TheScriptsAreNumberedWithoutGaps_SoNoMigrationCanBeSkippedOrReordered()
    {
        var ids = new EmbeddedMigrationSource().GetScripts().Select(s => s.Id).ToList();

        Assert.Equal(ids.Order(StringComparer.Ordinal), ids);
        Assert.Equal(Enumerable.Range(1, ids.Count).Select(n => $"{n:0000}_"), ids.Select(id => id[..5]));
    }

    [Fact]
    public async Task ASecondRun_ChangesNothing()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        await CreateRunner(db).ApplyAsync(CancellationToken.None);
        var before = await db.ScalarAsync<string>("SELECT string_agg(script_id || checksum_sha256, ',' ORDER BY script_id) FROM schema_migration");

        await CreateRunner(db).ApplyAsync(CancellationToken.None);

        Assert.Equal(before, await db.ScalarAsync<string>("SELECT string_agg(script_id || checksum_sha256, ',' ORDER BY script_id) FROM schema_migration"));
    }

    [Fact]
    public async Task AnEditedMigration_IsRefused_NamingTheScript()
    {
        await using var db = await postgres.CreateDatabaseAsync();
        await CreateRunner(db).ApplyAsync(CancellationToken.None);
        await db.ExecuteAsync("UPDATE schema_migration SET checksum_sha256 = repeat('0', 64) WHERE script_id LIKE '0003_%'");

        var ex = await Assert.ThrowsAsync<MigrationChecksumException>(() => CreateRunner(db).ApplyAsync(CancellationToken.None));

        Assert.StartsWith("0003_", ex.ScriptId);
    }
}
