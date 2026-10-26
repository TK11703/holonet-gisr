using Dapper;
using Gisr.Database.Common;
using Npgsql;

namespace Gisr.Database.Records;

public static class AccessLogKind
{
    public const string Record = "record";
    public const string Artifact = "artifact";
}

public sealed class AccessLogRow
{
    public Guid Id { get; set; }

    public Guid RecordId { get; set; }

    public Guid AccessedByAccountId { get; set; }

    public DateTimeOffset AccessedAt { get; set; }

    public string Kind { get; set; } = string.Empty;
}

public sealed class AccessLogRepository(NpgsqlDataSource dataSource, ISqlScriptLoader sql)
{
    public async Task InsertAsync(Guid recordId, Guid accessedByAccountId, string kind, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            sql.Get("Records", "insert-access-log"),
            new { Id = Guid.CreateVersion7(), RecordId = recordId, AccessedByAccountId = accessedByAccountId, Kind = kind },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<AccessLogRow>> ListByRecordAsync(Guid recordId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<AccessLogRow>(new CommandDefinition(
            sql.Get("Records", "list-access-log"), new { RecordId = recordId }, cancellationToken: cancellationToken));
        return [.. rows];
    }
}
