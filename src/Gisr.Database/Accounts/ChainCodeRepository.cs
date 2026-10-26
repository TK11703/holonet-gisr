using Dapper;
using Gisr.Database.Common;
using Npgsql;

namespace Gisr.Database.Accounts;

public sealed class ChainCodeRow
{
    public Guid AccountId { get; set; }
    public string ProfileJson { get; set; } = string.Empty;
    public string ChainCode { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ChainCodeRepository(NpgsqlDataSource dataSource, ISqlScriptLoader sql)
{
    public async Task<ChainCodeRow?> GetAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<ChainCodeRow>(new CommandDefinition(
            sql.Get("Accounts", "get-chain-code"), new { AccountId = accountId }, cancellationToken: cancellationToken));
    }

    public async Task<ChainCodeRow> UpsertAsync(Guid accountId, string profileJson, string chainCode, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleAsync<ChainCodeRow>(new CommandDefinition(
            sql.Get("Accounts", "upsert-chain-code"),
            new { AccountId = accountId, ProfileJson = profileJson, ChainCode = chainCode }, cancellationToken: cancellationToken));
    }

    public async Task<bool> DeleteAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(
            sql.Get("Accounts", "delete-chain-code"), new { AccountId = accountId }, cancellationToken: cancellationToken)) > 0;
    }
}