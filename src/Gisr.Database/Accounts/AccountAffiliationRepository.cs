using Dapper;
using Gisr.Database.Common;
using Npgsql;

namespace Gisr.Database.Accounts;

public sealed class AccountAffiliationRow
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string PrimaryAffiliation { get; set; } = string.Empty;
    public string? SecondaryAffiliation { get; set; }
    public string? SpecialUnit { get; set; }
    public string? Prefix { get; set; }
    public string? OperatingNumber { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public enum AccountAffiliationAddStatus
{
    Added,
    Duplicate,
    LimitReached,
}

public sealed record AccountAffiliationAddResult(AccountAffiliationAddStatus Status, AccountAffiliationRow? Row);

public sealed class AccountAffiliationRepository(NpgsqlDataSource dataSource, ISqlScriptLoader sql)
{
    public const int MaxPerAccount = 25;

    public async Task<IReadOnlyList<AccountAffiliationRow>> ListAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return (await connection.QueryAsync<AccountAffiliationRow>(new CommandDefinition(
            sql.Get("Accounts", "list-account-affiliations"), new { AccountId = accountId }, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<AccountAffiliationAddResult> AddAsync(
        Guid accountId, string primary, string? secondary, string? specialUnit, string? prefix, string? operatingNumber,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<AccountAffiliationRow>(new CommandDefinition(
            sql.Get("Accounts", "insert-account-affiliation"),
            new
            {
                Id = Guid.NewGuid(), AccountId = accountId, PrimaryAffiliation = primary, SecondaryAffiliation = secondary,
                SpecialUnit = specialUnit, Prefix = prefix, OperatingNumber = operatingNumber, MaxPerAccount,
            },
            cancellationToken: cancellationToken));
        if (row is not null) return new(AccountAffiliationAddStatus.Added, row);

        var existing = await ListAsync(accountId, cancellationToken);
        var duplicate = existing.Any(item => item.PrimaryAffiliation == primary
            && (item.SecondaryAffiliation ?? string.Empty) == (secondary ?? string.Empty)
            && (item.SpecialUnit ?? string.Empty) == (specialUnit ?? string.Empty));
        return new(duplicate ? AccountAffiliationAddStatus.Duplicate : AccountAffiliationAddStatus.LimitReached, null);
    }

    public async Task<bool> DeleteAsync(Guid accountId, Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(
            sql.Get("Accounts", "delete-account-affiliation"), new { Id = id, AccountId = accountId }, cancellationToken: cancellationToken)) > 0;
    }
}
