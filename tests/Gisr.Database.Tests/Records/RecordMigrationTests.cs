using Gisr.Database.Tests.Common;
using Npgsql;

namespace Gisr.Database.Tests.Records;

[Collection("postgres")]
public class RecordMigrationTests(PostgresFixture postgres)
{
    private const string CheckViolation = "23514";
    private const string UniqueViolation = "23505";
    private const string ForeignKeyViolation = "23503";

    private static string InsertRecord(Guid subject, Guid author, string title = "Title", string? start = null, string? end = null, Guid? id = null) =>
        $"""
        INSERT INTO record (id, subject_account_id, author_account_id, title, affiliation, engagement_start, engagement_end, created_at, updated_at, updated_by_account_id)
        VALUES ('{id ?? Guid.NewGuid()}', '{subject}', '{author}', '{title}', 'military', {Sql(start)}, {Sql(end)}, now(), now(), '{author}')
        """;

    private static string Sql(string? date) => date is null ? "NULL" : $"'{date}'";

    private static async Task<string> SqlStateAsync(Task task)
    {
        var ex = await Assert.ThrowsAsync<PostgresException>(() => task);
        return ex.SqlState;
    }

    [Fact]
    public async Task Record_TitleLength_MustBeBetween1And200()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var account = await RecordTestData.CreateAccountAsync(db);

        await db.ExecuteAsync(InsertRecord(account.Id, account.Id, new string('x', 200)));

        Assert.Equal(CheckViolation, await SqlStateAsync(db.ExecuteAsync(InsertRecord(account.Id, account.Id, new string('x', 201)))));
        Assert.Equal(CheckViolation, await SqlStateAsync(db.ExecuteAsync(InsertRecord(account.Id, account.Id, ""))));
    }

    [Fact]
    public async Task Record_EngagementEnd_MustNotPrecedeStart()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var account = await RecordTestData.CreateAccountAsync(db);

        await db.ExecuteAsync(InsertRecord(account.Id, account.Id, start: "2020-01-01", end: "2020-01-01"));
        await db.ExecuteAsync(InsertRecord(account.Id, account.Id, start: "2020-01-01", end: null));
        await db.ExecuteAsync(InsertRecord(account.Id, account.Id, start: null, end: "2019-01-01"));

        Assert.Equal(CheckViolation, await SqlStateAsync(db.ExecuteAsync(
            InsertRecord(account.Id, account.Id, start: "2020-01-02", end: "2020-01-01"))));
    }

    [Fact]
    public async Task Record_Defaults_VersionOneAndEmptyDetail()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var account = await RecordTestData.CreateAccountAsync(db);

        await db.ExecuteAsync(InsertRecord(account.Id, account.Id));

        Assert.Equal(1, await db.ScalarAsync<int>("SELECT version FROM record"));
        Assert.Equal("{}", await db.ScalarAsync<string>("SELECT engagement_detail::text FROM record"));
    }

    [Fact]
    public async Task Record_RequiresExistingSubjectAndAuthor()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var account = await RecordTestData.CreateAccountAsync(db);

        Assert.Equal(ForeignKeyViolation, await SqlStateAsync(db.ExecuteAsync(InsertRecord(Guid.NewGuid(), account.Id))));
        Assert.Equal(ForeignKeyViolation, await SqlStateAsync(db.ExecuteAsync(InsertRecord(account.Id, Guid.NewGuid()))));
    }

    [Fact]
    public async Task Revision_RecordAndVersion_MustBeUnique()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var account = await RecordTestData.CreateAccountAsync(db);
        var recordId = Guid.NewGuid();
        await db.ExecuteAsync(InsertRecord(account.Id, account.Id, id: recordId));
        var revision = $"INSERT INTO record_revision (id, record_id, version, changed_by_account_id, changed_at, snapshot) VALUES ('{{0}}', '{recordId}', 1, '{account.Id}', now(), '{{{{}}}}')";

        await db.ExecuteAsync(string.Format(revision, Guid.NewGuid()));

        Assert.Equal(UniqueViolation, await SqlStateAsync(db.ExecuteAsync(string.Format(revision, Guid.NewGuid()))));
    }

    [Fact]
    public async Task ChildRows_RequireExistingRecord()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();

        Assert.Equal(ForeignKeyViolation, await SqlStateAsync(db.ExecuteAsync(
            $"INSERT INTO record_incident (id, record_id, occurred_on, title, sort_order) VALUES ('{Guid.NewGuid()}', '{Guid.NewGuid()}', '2020-01-01', 'x', 0)")));
        Assert.Equal(ForeignKeyViolation, await SqlStateAsync(db.ExecuteAsync(
            $"INSERT INTO record_award (id, record_id, name, awarded_on, sort_order) VALUES ('{Guid.NewGuid()}', '{Guid.NewGuid()}', 'x', '2020-01-01', 0)")));
    }

    [Fact]
    public async Task IncidentAndAward_TextLimits_AreEnforced()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var account = await RecordTestData.CreateAccountAsync(db);
        var recordId = Guid.NewGuid();
        await db.ExecuteAsync(InsertRecord(account.Id, account.Id, id: recordId));

        Assert.Equal(CheckViolation, await SqlStateAsync(db.ExecuteAsync(
            $"INSERT INTO record_incident (id, record_id, occurred_on, title, sort_order) VALUES ('{Guid.NewGuid()}', '{recordId}', '2020-01-01', '{new string('x', 201)}', 0)")));
        Assert.Equal(CheckViolation, await SqlStateAsync(db.ExecuteAsync(
            $"INSERT INTO record_incident (id, record_id, occurred_on, title, description, sort_order) VALUES ('{Guid.NewGuid()}', '{recordId}', '2020-01-01', 'x', '{new string('x', 4001)}', 0)")));
        Assert.Equal(CheckViolation, await SqlStateAsync(db.ExecuteAsync(
            $"INSERT INTO record_award (id, record_id, name, awarded_on, issuer, sort_order) VALUES ('{Guid.NewGuid()}', '{recordId}', 'x', '2020-01-01', '{new string('x', 201)}', 0)")));
        Assert.Equal(CheckViolation, await SqlStateAsync(db.ExecuteAsync(
            $"INSERT INTO record_award (id, record_id, name, awarded_on, description, sort_order) VALUES ('{Guid.NewGuid()}', '{recordId}', 'x', '2020-01-01', '{new string('x', 2001)}', 0)")));
    }

    [Fact]
    public async Task Records_AreNeverCascadeDeleted()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var account = await RecordTestData.CreateAccountAsync(db);
        var recordId = Guid.NewGuid();
        await db.ExecuteAsync(InsertRecord(account.Id, account.Id, id: recordId));
        await db.ExecuteAsync(
            $"INSERT INTO record_incident (id, record_id, occurred_on, title, sort_order) VALUES ('{Guid.NewGuid()}', '{recordId}', '2020-01-01', 'x', 0)");

        Assert.Equal(ForeignKeyViolation, await SqlStateAsync(db.ExecuteAsync($"DELETE FROM record WHERE id = '{recordId}'")));
    }
}
