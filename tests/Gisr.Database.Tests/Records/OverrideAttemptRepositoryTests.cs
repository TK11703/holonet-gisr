using Gisr.Database.Common;
using Gisr.Database.Records;
using Gisr.Database.Tests.Common;

namespace Gisr.Database.Tests.Records;

[Collection("postgres")]
public class OverrideAttemptRepositoryTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private static OverrideAttemptRepository Attempts(TestDatabase db) => new(db.DataSource, new SqlScriptLoader());

    private static async Task<(Guid Subject, Guid Other, Guid RecordId)> SeedAsync(TestDatabase db)
    {
        var subject = await RecordTestData.CreateAccountAsync(db, "Subject");
        var other = await RecordTestData.CreateAccountAsync(db, "Other");
        var officer = await RecordTestData.CreateAccountAsync(db, "Officer");
        var record = await RecordTestData.Records(db).CreateAsync(subject.Id, officer.Id,
            RecordTestData.Write() with { IsSealed = true, OverrideCodeHash = "pbkdf2-sha256$1$salt$hash" }, Ct);
        return (subject.Id, other.Id, record.Record.Id);
    }

    [Fact]
    public async Task Insert_StoresTheOutcome_NeverTheCode()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var (subject, _, recordId) = await SeedAsync(db);

        await Attempts(db).InsertAsync(recordId, subject, false, Ct);
        await Attempts(db).InsertAsync(recordId, subject, true, Ct);

        Assert.Equal(2, await db.ScalarAsync<int>("SELECT count(*) FROM record_override_attempt"));
        Assert.Equal(1, await db.ScalarAsync<int>("SELECT count(*) FROM record_override_attempt WHERE succeeded"));
        Assert.Equal(["account_id", "attempted_at", "id", "record_id", "succeeded"],
            (await ColumnsAsync(db)).Order());
    }

    private static async Task<IEnumerable<string>> ColumnsAsync(TestDatabase db)
    {
        await using var command = db.DataSource.CreateCommand(
            "SELECT column_name FROM information_schema.columns WHERE table_name = 'record_override_attempt'");
        await using var reader = await command.ExecuteReaderAsync();
        var names = new List<string>();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    [Fact]
    public async Task Lockout_CountsOnlyRecentFailuresForThatRecordAndAccount()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var (subject, other, recordId) = await SeedAsync(db);
        var attempts = Attempts(db);
        await attempts.InsertAsync(recordId, subject, false, Ct);
        await attempts.InsertAsync(recordId, subject, false, Ct);
        await attempts.InsertAsync(recordId, subject, true, Ct);
        await attempts.InsertAsync(recordId, other, false, Ct);
        await db.ExecuteAsync(
            $"INSERT INTO record_override_attempt (id, record_id, account_id, attempted_at, succeeded) VALUES ('{Guid.NewGuid()}', '{recordId}', '{subject}', now() - interval '16 minutes', false)");

        var status = await attempts.GetLockoutAsync(recordId, subject, Window, Ct);

        Assert.Equal(2, status.Failures);
        Assert.Equal(1, (await attempts.GetLockoutAsync(recordId, other, Window, Ct)).Failures);
        Assert.Equal(0, (await attempts.GetLockoutAsync(Guid.NewGuid(), subject, Window, Ct)).Failures);
    }

    [Fact]
    public async Task Lockout_ReportsWhenTheOldestFailureLeavesTheWindow()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var (subject, _, recordId) = await SeedAsync(db);
        var minutesAgo = new[] { 14, 10, 8, 5, 1 };
        foreach (var minutes in minutesAgo)
        {
            await db.ExecuteAsync(
                $"INSERT INTO record_override_attempt (id, record_id, account_id, attempted_at, succeeded) VALUES ('{Guid.NewGuid()}', '{recordId}', '{subject}', now() - interval '{minutes} minutes', false)");
        }

        var status = await Attempts(db).GetLockoutAsync(recordId, subject, Window, Ct);

        Assert.Equal(5, status.Failures);
        Assert.InRange(status.RetryAfterSeconds, 50, 61);
    }

    [Fact]
    public async Task Lockout_Lifts_WhenTheOldestFailureIsFifteenMinutesOld()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var (subject, _, recordId) = await SeedAsync(db);
        foreach (var minutes in new[] { 16, 10, 8, 5, 1 })
        {
            await db.ExecuteAsync(
                $"INSERT INTO record_override_attempt (id, record_id, account_id, attempted_at, succeeded) VALUES ('{Guid.NewGuid()}', '{recordId}', '{subject}', now() - interval '{minutes} minutes', false)");
        }

        var status = await Attempts(db).GetLockoutAsync(recordId, subject, Window, Ct);

        Assert.Equal(4, status.Failures);
    }

    [Fact]
    public async Task Lockout_WithNoFailures_HasNoRetryDelay()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var (subject, _, recordId) = await SeedAsync(db);

        var status = await Attempts(db).GetLockoutAsync(recordId, subject, Window, Ct);

        Assert.Equal(0, status.Failures);
        Assert.Equal(0, status.RetryAfterSeconds);
    }
}
