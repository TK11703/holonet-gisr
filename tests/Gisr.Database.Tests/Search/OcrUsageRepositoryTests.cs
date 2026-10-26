using Gisr.Database.Search;
using Gisr.Database.Tests.Common;
using Gisr.Database.Tests.Records;

namespace Gisr.Database.Tests.Search;

[Collection("postgres")]
public class OcrUsageRepositoryTests(PostgresFixture postgres)
{
    private static readonly DateOnly October = new(2026, 10, 1);
    private static readonly DateOnly November = new(2026, 11, 1);

    private static OcrUsageRepository Usage(TestDatabase db) => new(db.DataSource, new Gisr.Database.Common.SqlScriptLoader());

    [Fact]
    public async Task AReservationWithinTheBudget_IsGranted_AndCounted()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var usage = Usage(db);

        Assert.True(await usage.TryReservePagesAsync(October, 30, 100, CancellationToken.None));
        Assert.True(await usage.TryReservePagesAsync(October, 70, 100, CancellationToken.None));

        Assert.Equal(100, await usage.GetPagesUsedAsync(October, CancellationToken.None));
    }

    [Fact]
    public async Task AReservationBeyondTheBudget_IsRefused_AndChangesNothing()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var usage = Usage(db);
        Assert.True(await usage.TryReservePagesAsync(October, 90, 100, CancellationToken.None));

        Assert.False(await usage.TryReservePagesAsync(October, 11, 100, CancellationToken.None));

        Assert.Equal(90, await usage.GetPagesUsedAsync(October, CancellationToken.None));
        Assert.True(await usage.TryReservePagesAsync(October, 10, 100, CancellationToken.None));
    }

    [Fact]
    public async Task EachMonthHasItsOwnBudget()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var usage = Usage(db);
        Assert.True(await usage.TryReservePagesAsync(October, 100, 100, CancellationToken.None));
        Assert.False(await usage.TryReservePagesAsync(October, 1, 100, CancellationToken.None));

        Assert.True(await usage.TryReservePagesAsync(November, 100, 100, CancellationToken.None));

        Assert.Equal(100, await usage.GetPagesUsedAsync(November, CancellationToken.None));
    }

    [Fact]
    public async Task AMonthWithoutUsage_ReportsZero()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();

        Assert.Equal(0, await Usage(db).GetPagesUsedAsync(October, CancellationToken.None));
    }

    [Fact]
    public async Task ConcurrentReservations_NeverExceedTheBudget()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var usage = Usage(db);

        var results = await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => usage.TryReservePagesAsync(October, 10, 200, CancellationToken.None)));

        Assert.Equal(20, results.Count(granted => granted));
        Assert.Equal(200, await usage.GetPagesUsedAsync(October, CancellationToken.None));
    }

    [Fact]
    public async Task ABudgetOfZero_RefusesEverything()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();

        Assert.False(await Usage(db).TryReservePagesAsync(October, 1, 0, CancellationToken.None));
    }

    [Fact]
    public async Task OnlyArtifactsSkippedForTheBudget_AreRequeued_OnTheFirstRunOfAMonth()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var account = await RecordTestData.CreateAccountAsync(db);
        var record = await RecordTestData.Records(db).CreateAsync(account.Id, account.Id, RecordTestData.Write(), CancellationToken.None);
        var budget = await Artifacts.ArtifactTestData.AddAsync(db, record.Record.Id, account.Id, "budget.pdf");
        var disabled = await Artifacts.ArtifactTestData.AddAsync(db, record.Record.Id, account.Id, "disabled.pdf");
        var removed = await Artifacts.ArtifactTestData.AddAsync(db, record.Record.Id, account.Id, "removed.pdf");
        await db.ExecuteAsync($"UPDATE artifact SET index_status = 'skipped', index_detail = 'ocr_budget_exhausted' WHERE id IN ('{budget.Id}', '{removed.Id}')");
        await db.ExecuteAsync($"UPDATE artifact SET index_status = 'skipped', index_detail = 'ocr_disabled' WHERE id = '{disabled.Id}'");
        await Artifacts.ArtifactTestData.Artifacts(db).RemoveAsync(removed.Id, account.Id, CancellationToken.None);
        await db.ExecuteAsync("DELETE FROM search_index_job");
        var usage = Usage(db);

        Assert.Equal(1, await usage.RequeueBudgetSkippedAsync(October, CancellationToken.None));

        Assert.Equal("pending", await db.ScalarAsync<string>($"SELECT index_status FROM artifact WHERE id = '{budget.Id}'"));
        Assert.Null(await db.ScalarAsync<string>($"SELECT index_detail FROM artifact WHERE id = '{budget.Id}'"));
        Assert.Equal("ocr_disabled", await db.ScalarAsync<string>($"SELECT index_detail FROM artifact WHERE id = '{disabled.Id}'"));
        Assert.Equal(1L, await db.ScalarAsync<long>($"SELECT count(*) FROM search_index_job WHERE artifact_id = '{budget.Id}' AND status = 'pending'"));

        await db.ExecuteAsync($"UPDATE artifact SET index_status = 'skipped', index_detail = 'ocr_budget_exhausted' WHERE id = '{budget.Id}'");
        Assert.Equal(0, await usage.RequeueBudgetSkippedAsync(October, CancellationToken.None));
        Assert.Equal("skipped", await db.ScalarAsync<string>($"SELECT index_status FROM artifact WHERE id = '{budget.Id}'"));
        Assert.Equal(1, await usage.RequeueBudgetSkippedAsync(November, CancellationToken.None));
    }

    [Fact]
    public async Task TheRequeueRuns_EvenIfAReservationCreatedTheMonthRowFirst()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var account = await RecordTestData.CreateAccountAsync(db);
        var record = await RecordTestData.Records(db).CreateAsync(account.Id, account.Id, RecordTestData.Write(), CancellationToken.None);
        var artifact = await Artifacts.ArtifactTestData.AddAsync(db, record.Record.Id, account.Id);
        await db.ExecuteAsync($"UPDATE artifact SET index_status = 'skipped', index_detail = 'ocr_budget_exhausted' WHERE id = '{artifact.Id}'");
        var usage = Usage(db);
        await usage.TryReservePagesAsync(November, 5, 100, CancellationToken.None);

        Assert.Equal(1, await usage.RequeueBudgetSkippedAsync(November, CancellationToken.None));
    }
}
