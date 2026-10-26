using Dapper;
using Gisr.Database.Common;
using Npgsql;

namespace Gisr.Database.Records;

public sealed class ViewGrantRepository(NpgsqlDataSource dataSource, ISqlScriptLoader sql)
{
    // The raw token never reaches the database; callers pass its SHA-256 hash.
    public async Task<DateTimeOffset> InsertAsync(Guid recordId, Guid accountId, byte[] tokenHash, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<DateTimeOffset>(new CommandDefinition(
            sql.Get("Records", "insert-view-grant"),
            new { Id = Guid.CreateVersion7(), RecordId = recordId, AccountId = accountId, TokenHash = tokenHash },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> IsValidAsync(Guid recordId, Guid accountId, byte[] tokenHash, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            sql.Get("Records", "get-valid-view-grant"),
            new { RecordId = recordId, AccountId = accountId, TokenHash = tokenHash },
            cancellationToken: cancellationToken));
    }

    public async Task RevokeAsync(Guid recordId, Guid accountId, byte[] tokenHash, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            sql.Get("Records", "revoke-view-grant"),
            new { RecordId = recordId, AccountId = accountId, TokenHash = tokenHash },
            cancellationToken: cancellationToken));
    }

    public async Task DeleteForRecordAsync(Guid recordId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            sql.Get("Records", "delete-view-grants-for-record"), new { RecordId = recordId }, cancellationToken: cancellationToken));
    }

    public async Task<int> PurgeAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(sql.Get("Records", "purge-view-grants"), cancellationToken: cancellationToken));
    }
}
