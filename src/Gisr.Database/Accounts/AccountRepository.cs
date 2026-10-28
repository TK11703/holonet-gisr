using Dapper;
using Gisr.Database.Common;
using Npgsql;

namespace Gisr.Database.Accounts;

public sealed class AccountRepository(NpgsqlDataSource dataSource, ISqlScriptLoader sql)
{
    public async Task<AccountRow?> UpdateOperatingNumberAsync(Guid id, string? operatingNumber, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<AccountRow>(new CommandDefinition(
            sql.Get("Accounts", "update-operating-number"), new { Id = id, OperatingNumber = operatingNumber },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<AccountRow>> SearchByOperatingNumberAsync(
        Guid tenantId,
        string term,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<AccountRow>(new CommandDefinition(
            sql.Get("Accounts", "search-accounts-by-operating-number"),
            new { TenantId = tenantId, Term = term, Limit = limit },
            cancellationToken: cancellationToken));
        return [.. rows];
    }

    public async Task<AccountRow> UpsertAsync(
        Guid tenantId,
        Guid entraObjectId,
        string displayName,
        string? signInEmail,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleAsync<AccountRow>(new CommandDefinition(
            sql.Get("Accounts", "upsert-account"),
            new { Id = Guid.CreateVersion7(), TenantId = tenantId, EntraObjectId = entraObjectId, DisplayName = displayName, SignInEmail = signInEmail },
            cancellationToken: cancellationToken));
    }

    public async Task<AccountRow?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<AccountRow>(new CommandDefinition(
            sql.Get("Accounts", "get-account-by-id"), new { Id = id }, cancellationToken: cancellationToken));
    }

    public async Task<AccountRow?> FindByObjectAsync(Guid tenantId, Guid entraObjectId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<AccountRow>(new CommandDefinition(
            sql.Get("Accounts", "get-account-by-object"),
            new { TenantId = tenantId, EntraObjectId = entraObjectId },
            cancellationToken: cancellationToken));
    }

    // Created for a directory user who has not signed in yet; the first sign-in upsert adopts the row.
    public async Task<AccountRow> EnsurePlaceholderAsync(
        Guid tenantId,
        Guid entraObjectId,
        string displayName,
        string? signInEmail,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleAsync<AccountRow>(new CommandDefinition(
            sql.Get("Accounts", "ensure-account"),
            new { Id = Guid.CreateVersion7(), TenantId = tenantId, EntraObjectId = entraObjectId, DisplayName = displayName, SignInEmail = signInEmail },
            cancellationToken: cancellationToken));
    }

    public async Task<AccountRow?> UpdateSettingsAsync(
        Guid id,
        string displayName,
        string? contactEmail,
        bool notifyOnServiceRecord,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<AccountRow>(new CommandDefinition(
            sql.Get("Accounts", "update-account-settings"),
            new { Id = id, DisplayName = displayName, ContactEmail = contactEmail, NotifyOnServiceRecord = notifyOnServiceRecord },
            cancellationToken: cancellationToken));
    }
}
