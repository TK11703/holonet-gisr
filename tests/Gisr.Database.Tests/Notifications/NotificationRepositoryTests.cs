using Gisr.Database.Common;
using Gisr.Database.Notifications;
using Gisr.Database.Tests.Common;
using Gisr.Database.Tests.Records;
using Npgsql;

namespace Gisr.Database.Tests.Notifications;

[Collection("postgres")]
public class NotificationRepositoryTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static NotificationRepository Repository(TestDatabase db) => new(db.DataSource, new SqlScriptLoader());

    private static NotificationWrite Note(Guid recipient, string message, Guid? recordId = null) =>
        new(recipient, NotificationKind.ServiceRecordCreated, recordId, message);

    [Fact]
    public async Task Insert_StoresAnUnreadNotificationForTheRecipient()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var recipient = await RecordTestData.CreateAccountAsync(db);
        var record = await RecordTestData.Records(db).CreateAsync(recipient.Id, recipient.Id, RecordTestData.Write(), Ct);

        await Repository(db).InsertAsync(Note(recipient.Id, "A service record was created for you.", record.Record.Id), Ct);

        var list = await Repository(db).ListAsync(recipient.Id, false, 20, Ct);
        var row = Assert.Single(list.Items);
        Assert.Equal(recipient.Id, row.RecipientAccountId);
        Assert.Equal("service_record_created", row.Kind);
        Assert.Equal(record.Record.Id, row.RecordId);
        Assert.Equal("A service record was created for you.", row.Message);
        Assert.Null(row.ReadAt);
        Assert.Equal(TimeSpan.Zero, row.CreatedAt.Offset);
    }

    [Fact]
    public async Task Insert_AllowsNotificationsWithoutARecord()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var recipient = await RecordTestData.CreateAccountAsync(db);

        await Repository(db).InsertAsync(Note(recipient.Id, "No record"), Ct);

        Assert.Null(Assert.Single((await Repository(db).ListAsync(recipient.Id, false, 20, Ct)).Items).RecordId);
    }

    [Fact]
    public async Task Insert_RejectsUnknownKindsRecipientsAndRecords()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var recipient = await RecordTestData.CreateAccountAsync(db);
        var repository = Repository(db);

        var kind = await Assert.ThrowsAsync<PostgresException>(() =>
            repository.InsertAsync(new NotificationWrite(recipient.Id, "newsletter", null, "x"), Ct));
        var unknownRecipient = await Assert.ThrowsAsync<PostgresException>(() => repository.InsertAsync(Note(Guid.NewGuid(), "x"), Ct));
        var unknownRecord = await Assert.ThrowsAsync<PostgresException>(() => repository.InsertAsync(Note(recipient.Id, "x", Guid.NewGuid()), Ct));

        Assert.Equal("23514", kind.SqlState);
        Assert.Equal("23503", unknownRecipient.SqlState);
        Assert.Equal("23503", unknownRecord.SqlState);
    }

    [Fact]
    public async Task List_ReturnsNewestFirst_WithTheTotalUnreadCount()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var recipient = await RecordTestData.CreateAccountAsync(db);
        var repository = Repository(db);
        foreach (var message in new[] { "first", "second", "third" })
        {
            await repository.InsertAsync(Note(recipient.Id, message), Ct);
        }

        var list = await repository.ListAsync(recipient.Id, false, 2, Ct);

        Assert.Equal(["third", "second"], list.Items.Select(n => n.Message));
        Assert.Equal(3, list.UnreadCount);
    }

    [Fact]
    public async Task List_OnlyReturnsTheRecipientsNotifications()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var alice = await RecordTestData.CreateAccountAsync(db);
        var bob = await RecordTestData.CreateAccountAsync(db);
        var repository = Repository(db);
        await repository.InsertAsync(Note(alice.Id, "for alice"), Ct);
        await repository.InsertAsync(Note(bob.Id, "for bob"), Ct);

        var list = await repository.ListAsync(alice.Id, false, 20, Ct);

        Assert.Equal(["for alice"], list.Items.Select(n => n.Message));
        Assert.Equal(1, list.UnreadCount);
    }

    [Fact]
    public async Task List_UnreadOnly_HidesReadNotifications()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var recipient = await RecordTestData.CreateAccountAsync(db);
        var repository = Repository(db);
        await repository.InsertAsync(Note(recipient.Id, "read me"), Ct);
        await repository.InsertAsync(Note(recipient.Id, "still unread"), Ct);
        var all = await repository.ListAsync(recipient.Id, false, 20, Ct);
        await repository.MarkReadAsync(all.Items.Single(n => n.Message == "read me").Id, recipient.Id, Ct);

        var unread = await repository.ListAsync(recipient.Id, true, 20, Ct);
        var everything = await repository.ListAsync(recipient.Id, false, 20, Ct);

        Assert.Equal(["still unread"], unread.Items.Select(n => n.Message));
        Assert.Equal(1, unread.UnreadCount);
        Assert.Equal(2, everything.Items.Count);
        Assert.NotNull(everything.Items.Single(n => n.Message == "read me").ReadAt);
    }

    [Fact]
    public async Task MarkRead_OnlyWorksForTheRecipient()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var alice = await RecordTestData.CreateAccountAsync(db);
        var bob = await RecordTestData.CreateAccountAsync(db);
        var repository = Repository(db);
        await repository.InsertAsync(Note(alice.Id, "private"), Ct);
        var id = (await repository.ListAsync(alice.Id, false, 20, Ct)).Items.Single().Id;

        var byOther = await repository.MarkReadAsync(id, bob.Id, Ct);
        var unknown = await repository.MarkReadAsync(Guid.NewGuid(), alice.Id, Ct);
        var stillUnread = (await repository.ListAsync(alice.Id, false, 20, Ct)).UnreadCount;
        var byRecipient = await repository.MarkReadAsync(id, alice.Id, Ct);
        var again = await repository.MarkReadAsync(id, alice.Id, Ct);

        Assert.False(byOther);
        Assert.False(unknown);
        Assert.Equal(1, stillUnread);
        Assert.True(byRecipient);
        Assert.True(again);
        Assert.Equal(0, (await repository.ListAsync(alice.Id, false, 20, Ct)).UnreadCount);
    }

    [Fact]
    public async Task MarkRead_KeepsTheOriginalReadTimestamp()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var recipient = await RecordTestData.CreateAccountAsync(db);
        var repository = Repository(db);
        await repository.InsertAsync(Note(recipient.Id, "x"), Ct);
        var id = (await repository.ListAsync(recipient.Id, false, 20, Ct)).Items.Single().Id;

        await repository.MarkReadAsync(id, recipient.Id, Ct);
        var first = (await repository.ListAsync(recipient.Id, false, 20, Ct)).Items.Single().ReadAt;
        await repository.MarkReadAsync(id, recipient.Id, Ct);
        var second = (await repository.ListAsync(recipient.Id, false, 20, Ct)).Items.Single().ReadAt;

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task MarkAllRead_AffectsOnlyTheRecipientsUnreadNotifications()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var alice = await RecordTestData.CreateAccountAsync(db);
        var bob = await RecordTestData.CreateAccountAsync(db);
        var repository = Repository(db);
        await repository.InsertAsync(Note(alice.Id, "a1"), Ct);
        await repository.InsertAsync(Note(alice.Id, "a2"), Ct);
        await repository.InsertAsync(Note(bob.Id, "b1"), Ct);

        await repository.MarkAllReadAsync(alice.Id, Ct);

        Assert.Equal(0, (await repository.ListAsync(alice.Id, false, 20, Ct)).UnreadCount);
        Assert.Equal(1, (await repository.ListAsync(bob.Id, false, 20, Ct)).UnreadCount);
    }

    [Fact]
    public async Task TheUnreadIndexCoversRecipientReadStateAndNewestFirst()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();

        var definition = await db.ScalarAsync<string>(
            "SELECT indexdef FROM pg_indexes WHERE tablename = 'notification' AND indexdef ILIKE '%recipient_account_id%read_at%'");

        Assert.NotNull(definition);
        Assert.Contains("(recipient_account_id, read_at, created_at DESC)", definition, StringComparison.Ordinal);
    }
}
