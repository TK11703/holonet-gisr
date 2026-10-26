using Gisr.Database.Records;
using Gisr.Database.Tests.Common;
using Npgsql;

namespace Gisr.Database.Tests.Records;

[Collection("postgres")]
public class AccessLogRepositoryTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static AccessLogRepository Log(TestDatabase db) => new(db.DataSource, new Gisr.Database.Common.SqlScriptLoader());

    private static async Task<(Guid Subject, Guid Viewer, Guid RecordId)> SeedAsync(TestDatabase db)
    {
        var subject = await RecordTestData.CreateAccountAsync(db, "Subject");
        var viewer = await RecordTestData.CreateAccountAsync(db, "Viewer");
        var record = await RecordTestData.Records(db).CreateAsync(subject.Id, subject.Id, RecordTestData.Write(), Ct);
        return (subject.Id, viewer.Id, record.Record.Id);
    }

    [Fact]
    public async Task Insert_RecordsWhoReadWhichRecord_AndWhen()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var (_, viewer, recordId) = await SeedAsync(db);

        await Log(db).InsertAsync(recordId, viewer, AccessLogKind.Record, Ct);

        var entry = Assert.Single(await Log(db).ListByRecordAsync(recordId, Ct));
        Assert.Equal(recordId, entry.RecordId);
        Assert.Equal(viewer, entry.AccessedByAccountId);
        Assert.Equal("record", entry.Kind);
        Assert.Equal(TimeSpan.Zero, entry.AccessedAt.Offset);
        Assert.True(entry.AccessedAt > DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task Insert_AcceptsArtifactKind()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var (_, viewer, recordId) = await SeedAsync(db);

        await Log(db).InsertAsync(recordId, viewer, AccessLogKind.Artifact, Ct);

        Assert.Equal("artifact", Assert.Single(await Log(db).ListByRecordAsync(recordId, Ct)).Kind);
    }

    [Fact]
    public async Task Insert_RejectsUnknownKinds()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var (_, viewer, recordId) = await SeedAsync(db);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => Log(db).InsertAsync(recordId, viewer, "search", Ct));

        Assert.Equal("23514", ex.SqlState);
    }

    [Fact]
    public async Task Insert_RequiresExistingRecordAndAccount()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var (_, viewer, recordId) = await SeedAsync(db);

        var unknownRecord = await Assert.ThrowsAsync<PostgresException>(() => Log(db).InsertAsync(Guid.NewGuid(), viewer, AccessLogKind.Record, Ct));
        var unknownAccount = await Assert.ThrowsAsync<PostgresException>(() => Log(db).InsertAsync(recordId, Guid.NewGuid(), AccessLogKind.Record, Ct));

        Assert.Equal("23503", unknownRecord.SqlState);
        Assert.Equal("23503", unknownAccount.SqlState);
    }

    [Fact]
    public async Task ListByRecord_ReturnsOnlyThatRecordsEntries_NewestFirst()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var (subject, viewer, recordId) = await SeedAsync(db);
        var other = await RecordTestData.Records(db).CreateAsync(subject, subject, RecordTestData.Write(title: "Other"), Ct);
        await Log(db).InsertAsync(recordId, viewer, AccessLogKind.Record, Ct);
        await Log(db).InsertAsync(other.Record.Id, viewer, AccessLogKind.Record, Ct);
        await Log(db).InsertAsync(recordId, subject, AccessLogKind.Artifact, Ct);

        var entries = await Log(db).ListByRecordAsync(recordId, Ct);

        Assert.Equal(2, entries.Count);
        Assert.Equal(["artifact", "record"], entries.Select(e => e.Kind));
        Assert.All(entries, e => Assert.Equal(recordId, e.RecordId));
    }
}
