using Dapper;
using Gisr.Database.Common;
using Npgsql;

namespace Gisr.Database.Notifications;

public static class NotificationKind
{
    public const string ServiceRecordCreated = "service_record_created";
}

public sealed record NotificationWrite(Guid RecipientAccountId, string Kind, Guid? RecordId, string Message);

public sealed class NotificationRow
{
    public Guid Id { get; set; }

    public Guid RecipientAccountId { get; set; }

    public string Kind { get; set; } = string.Empty;

    public Guid? RecordId { get; set; }

    public string Message { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ReadAt { get; set; }
}

public sealed record NotificationList(long UnreadCount, IReadOnlyList<NotificationRow> Items);

public sealed class NotificationRepository(NpgsqlDataSource dataSource, ISqlScriptLoader sql)
{
    public async Task InsertAsync(NotificationWrite write, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            sql.Get("Notifications", "insert-notification"),
            new { Id = Guid.CreateVersion7(), write.RecipientAccountId, write.Kind, write.RecordId, write.Message },
            cancellationToken: cancellationToken));
    }

    public async Task<NotificationList> ListAsync(Guid recipientAccountId, bool unreadOnly, int pageSize, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<NotificationRow>(new CommandDefinition(
            sql.Get("Notifications", "list-notifications"),
            new { RecipientAccountId = recipientAccountId, UnreadOnly = unreadOnly, PageSize = pageSize },
            cancellationToken: cancellationToken));
        var unread = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            sql.Get("Notifications", "count-unread-notifications"),
            new { RecipientAccountId = recipientAccountId },
            cancellationToken: cancellationToken));
        return new NotificationList(unread, [.. rows]);
    }

    public async Task<bool> MarkReadAsync(Guid id, Guid recipientAccountId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            sql.Get("Notifications", "mark-notification-read"),
            new { Id = id, RecipientAccountId = recipientAccountId },
            cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task MarkAllReadAsync(Guid recipientAccountId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            sql.Get("Notifications", "mark-all-read"),
            new { RecipientAccountId = recipientAccountId },
            cancellationToken: cancellationToken));
    }
}
