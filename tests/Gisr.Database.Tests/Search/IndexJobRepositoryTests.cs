using Gisr.Database.Search;
using Gisr.Database.Tests.Common;
using Gisr.Database.Tests.Records;

namespace Gisr.Database.Tests.Search;

[Collection("postgres")]
public class IndexJobRepositoryTests(PostgresFixture postgres)
{
    private static IndexJobRepository Jobs(TestDatabase db) => new(db.DataSource, new Gisr.Database.Common.SqlScriptLoader());

    private static async Task<Guid> RecordAsync(TestDatabase db)
    {
        var account = await RecordTestData.CreateAccountAsync(db);
        return (await RecordTestData.Records(db).CreateAsync(account.Id, account.Id, RecordTestData.Write(), CancellationToken.None)).Record.Id;
    }

    // Creating a record enqueues its own job, so tests start from a clean queue.
    private static Task ClearQueueAsync(TestDatabase db) => db.ExecuteAsync("DELETE FROM search_index_job");

    [Fact]
    public async Task ClaimingAJob_MovesItToRunning_AndCountsTheAttempt()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var recordId = await RecordAsync(db);
        await ClearQueueAsync(db);
        var repository = Jobs(db);
        await repository.EnqueueAsync(IndexJobKind.Record, recordId, null, CancellationToken.None);

        var job = await repository.ClaimAsync(CancellationToken.None);

        Assert.NotNull(job);
        Assert.Equal(IndexJobKind.Record, job.Kind);
        Assert.Equal(recordId, job.RecordId);
        Assert.Null(job.ArtifactId);
        Assert.Equal(1, job.Attempts);
        Assert.Equal("running", await db.ScalarAsync<string>("SELECT status FROM search_index_job"));
        Assert.Null(await repository.ClaimAsync(CancellationToken.None));
    }

    [Fact]
    public async Task WithNothingPending_ClaimReturnsNull()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();

        Assert.Null(await Jobs(db).ClaimAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CompletingAJob_MarksItSucceeded()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var recordId = await RecordAsync(db);
        await ClearQueueAsync(db);
        var repository = Jobs(db);
        await repository.EnqueueAsync(IndexJobKind.Record, recordId, null, CancellationToken.None);
        var job = (await repository.ClaimAsync(CancellationToken.None))!;

        await repository.CompleteAsync(job.Id, CancellationToken.None);

        Assert.Equal("succeeded", await db.ScalarAsync<string>("SELECT status FROM search_index_job"));
        Assert.Null(await repository.ClaimAsync(CancellationToken.None));
    }

    [Fact]
    public async Task TwoConcurrentWorkers_ClaimDisjointJobs()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var account = await RecordTestData.CreateAccountAsync(db);
        for (var i = 0; i < 8; i++)
        {
            await RecordTestData.Records(db).CreateAsync(account.Id, account.Id, RecordTestData.Write(title: $"Record {i}"), CancellationToken.None);
        }

        var repository = Jobs(db);
        var claimed = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => repository.ClaimAsync(CancellationToken.None)));

        var ids = claimed.Where(job => job is not null).Select(job => job!.Id).ToList();
        Assert.Equal(8, ids.Count);
        Assert.Equal(8, ids.Distinct().Count());
        Assert.Equal(8, claimed.Count(job => job is null));
    }

    [Fact]
    public async Task AFailedAttempt_IsRetriedLater_WithExponentialBackoff()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var recordId = await RecordAsync(db);
        await ClearQueueAsync(db);
        var repository = Jobs(db);
        await repository.EnqueueAsync(IndexJobKind.Record, recordId, null, CancellationToken.None);
        var first = (await repository.ClaimAsync(CancellationToken.None))!;

        await repository.FailAsync(first.Id, first.Attempts, "HttpRequestException", CancellationToken.None);

        Assert.Equal("pending", await db.ScalarAsync<string>("SELECT status FROM search_index_job"));
        Assert.Equal("HttpRequestException", await db.ScalarAsync<string>("SELECT last_error FROM search_index_job"));
        var delay = await db.ScalarAsync<double>("SELECT extract(epoch FROM available_at - now()) FROM search_index_job");
        Assert.InRange(delay, IndexJobBackoff.For(1).TotalSeconds - 5, IndexJobBackoff.For(1).TotalSeconds + 1);
        Assert.Null(await repository.ClaimAsync(CancellationToken.None));
    }

    [Fact]
    public void TheBackoff_DoublesWithEveryAttempt()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), IndexJobBackoff.For(1));
        Assert.Equal(TimeSpan.FromSeconds(60), IndexJobBackoff.For(2));
        Assert.Equal(TimeSpan.FromSeconds(120), IndexJobBackoff.For(3));
        Assert.Equal(TimeSpan.FromSeconds(240), IndexJobBackoff.For(4));
    }

    [Fact]
    public async Task ADeferredJob_GivesTheAttemptBack_SoAnOutageNeverUsesUpTheRetries()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var recordId = await RecordAsync(db);
        await ClearQueueAsync(db);
        var repository = Jobs(db);
        await repository.EnqueueAsync(IndexJobKind.Record, recordId, null, CancellationToken.None);

        for (var round = 1; round <= IndexJobRepository.MaxAttempts + 3; round++)
        {
            await db.ExecuteAsync("UPDATE search_index_job SET available_at = now() - interval '1 second' WHERE status = 'pending'");
            var job = (await repository.ClaimAsync(CancellationToken.None))!;
            Assert.Equal(1, job.Attempts);
            await repository.DeferAsync(job.Id, "HttpRequestException", IndexJobBackoff.Outage, CancellationToken.None);
        }

        Assert.Equal("pending", await db.ScalarAsync<string>("SELECT status FROM search_index_job"));
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT attempts FROM search_index_job"));
        Assert.Equal("HttpRequestException", await db.ScalarAsync<string>("SELECT last_error FROM search_index_job"));
        var delay = await db.ScalarAsync<double>("SELECT extract(epoch FROM available_at - now()) FROM search_index_job");
        Assert.InRange(delay, IndexJobBackoff.Outage.TotalSeconds - 5, IndexJobBackoff.Outage.TotalSeconds + 1);
        Assert.Null(await repository.ClaimAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AJob_FailsForGoodAfterFiveAttempts()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var recordId = await RecordAsync(db);
        await ClearQueueAsync(db);
        var repository = Jobs(db);
        await repository.EnqueueAsync(IndexJobKind.Record, recordId, null, CancellationToken.None);

        for (var attempt = 1; attempt <= IndexJobRepository.MaxAttempts; attempt++)
        {
            await db.ExecuteAsync("UPDATE search_index_job SET available_at = now() - interval '1 second' WHERE status = 'pending'");
            var job = (await repository.ClaimAsync(CancellationToken.None))!;
            Assert.Equal(attempt, job.Attempts);
            await repository.FailAsync(job.Id, job.Attempts, "TimeoutException", CancellationToken.None);
        }

        Assert.Equal("failed", await db.ScalarAsync<string>("SELECT status FROM search_index_job"));
        Assert.Equal(5, await db.ScalarAsync<int>("SELECT attempts FROM search_index_job"));
        await db.ExecuteAsync("UPDATE search_index_job SET available_at = now() - interval '1 second'");
        Assert.Null(await repository.ClaimAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AJobThatWasRunningWhenAWorkerDied_IsClaimedAgainAfterAWhile()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var recordId = await RecordAsync(db);
        await ClearQueueAsync(db);
        var repository = Jobs(db);
        await repository.EnqueueAsync(IndexJobKind.Record, recordId, null, CancellationToken.None);
        await repository.ClaimAsync(CancellationToken.None);
        Assert.Null(await repository.ClaimAsync(CancellationToken.None));

        await db.ExecuteAsync("UPDATE search_index_job SET updated_at = now() - interval '1 hour'");

        var again = await repository.ClaimAsync(CancellationToken.None);
        Assert.NotNull(again);
        Assert.Equal(2, again.Attempts);
    }

    [Fact]
    public async Task EnqueueingTheSameTargetTwice_KeepsOnePendingJob_ButAddsOneWhileItIsRunning()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var recordId = await RecordAsync(db);
        await ClearQueueAsync(db);
        var repository = Jobs(db);

        await repository.EnqueueAsync(IndexJobKind.Record, recordId, null, CancellationToken.None);
        await repository.EnqueueAsync(IndexJobKind.Record, recordId, null, CancellationToken.None);
        Assert.Equal(1L, await db.ScalarAsync<long>("SELECT count(*) FROM search_index_job"));

        await repository.ClaimAsync(CancellationToken.None);
        await repository.EnqueueAsync(IndexJobKind.Record, recordId, null, CancellationToken.None);

        Assert.Equal(2L, await db.ScalarAsync<long>("SELECT count(*) FROM search_index_job"));
    }

    [Fact]
    public async Task ArtifactJobs_CarryTheirArtifactId()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var account = await RecordTestData.CreateAccountAsync(db);
        var record = await RecordTestData.Records(db).CreateAsync(account.Id, account.Id, RecordTestData.Write(), CancellationToken.None);
        var artifact = await Artifacts.ArtifactTestData.AddAsync(db, record.Record.Id, account.Id);
        var repository = Jobs(db);
        await ClearQueueAsync(db);

        await repository.EnqueueAsync(IndexJobKind.Artifact, record.Record.Id, artifact.Id, CancellationToken.None);
        var job = await repository.ClaimAsync(CancellationToken.None);

        Assert.Equal(IndexJobKind.Artifact, job!.Kind);
        Assert.Equal(artifact.Id, job.ArtifactId);
    }

    [Fact]
    public async Task SavingARecord_AndFinishingAnUpload_EnqueueJobsInTheSameTransaction()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var account = await RecordTestData.CreateAccountAsync(db);

        var record = await RecordTestData.Records(db).CreateAsync(account.Id, account.Id, RecordTestData.Write(), CancellationToken.None);
        Assert.Equal(1L, await db.ScalarAsync<long>($"SELECT count(*) FROM search_index_job WHERE kind = 'record' AND record_id = '{record.Record.Id}'"));

        await Jobs(db).ClaimAsync(CancellationToken.None);
        var updated = await RecordTestData.Records(db).UpdateAsync(
            record.Record.Id, new Gisr.Database.Records.RecordCaller(account.Id), record.Record.Version, account.Id,
            RecordTestData.Write(title: "Changed"), CancellationToken.None);
        Assert.Equal(Gisr.Database.Records.RecordUpdateOutcome.Updated, updated.Outcome);
        Assert.Equal(1L, await db.ScalarAsync<long>("SELECT count(*) FROM search_index_job WHERE status = 'pending'"));

        var artifact = await Artifacts.ArtifactTestData.AddAsync(db, record.Record.Id, account.Id);
        Assert.Equal(1L, await db.ScalarAsync<long>($"SELECT count(*) FROM search_index_job WHERE kind = 'artifact' AND artifact_id = '{artifact.Id}' AND status = 'pending'"));
    }

    [Fact]
    public async Task AFailedUpdate_LeavesNoJobBehind()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var account = await RecordTestData.CreateAccountAsync(db);
        var record = await RecordTestData.Records(db).CreateAsync(account.Id, account.Id, RecordTestData.Write(), CancellationToken.None);
        await ClearQueueAsync(db);

        var stale = await RecordTestData.Records(db).UpdateAsync(
            record.Record.Id, new Gisr.Database.Records.RecordCaller(account.Id), record.Record.Version + 5, account.Id,
            RecordTestData.Write(title: "Stale"), CancellationToken.None);

        Assert.Equal(Gisr.Database.Records.RecordUpdateOutcome.Conflict, stale.Outcome);
        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT count(*) FROM search_index_job"));
    }

    [Fact]
    public async Task ARefusedArtifactInsert_LeavesNoJobBehind()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var account = await RecordTestData.CreateAccountAsync(db);
        var record = await RecordTestData.Records(db).CreateAsync(account.Id, account.Id, RecordTestData.Write(), CancellationToken.None);
        for (var i = 0; i < Gisr.Database.Artifacts.ArtifactRepository.MaxActivePerRecord; i++)
        {
            await Artifacts.ArtifactTestData.AddAsync(db, record.Record.Id, account.Id, $"f{i}.pdf");
        }

        await ClearQueueAsync(db);
        var refused = await Artifacts.ArtifactTestData.Artifacts(db).InsertAsync(
            Artifacts.ArtifactTestData.Write(record.Record.Id, account.Id), CancellationToken.None);

        Assert.Null(refused);
        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT count(*) FROM search_index_job"));
    }
}
