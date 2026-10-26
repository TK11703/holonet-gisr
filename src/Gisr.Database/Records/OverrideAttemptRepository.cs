using Dapper;
using Gisr.Database.Common;
using Npgsql;

namespace Gisr.Database.Records;

public sealed record OverrideLockout(long Failures, int RetryAfterSeconds);

public sealed class OverrideAttemptRepository(NpgsqlDataSource dataSource, ISqlScriptLoader sql)
{
    private sealed class LockoutRow
    {
        public long Failures { get; set; }

        public int RetryAfterSeconds { get; set; }
    }

    // Only the outcome is stored; the code that was entered never is.
    public async Task InsertAsync(Guid recordId, Guid accountId, bool succeeded, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            sql.Get("Records", "insert-override-attempt"),
            new { Id = Guid.CreateVersion7(), RecordId = recordId, AccountId = accountId, Succeeded = succeeded },
            cancellationToken: cancellationToken));
    }

    public async Task<OverrideLockout> GetLockoutAsync(Guid recordId, Guid accountId, TimeSpan window, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleAsync<LockoutRow>(new CommandDefinition(
            sql.Get("Records", "count-recent-failures"),
            new { RecordId = recordId, AccountId = accountId, WindowSeconds = (int)window.TotalSeconds },
            cancellationToken: cancellationToken));
        return new OverrideLockout(row.Failures, row.RetryAfterSeconds);
    }
}
