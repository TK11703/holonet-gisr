using Dapper;
using Gisr.Database.Common;
using Npgsql;

namespace Gisr.Database.Search;

public static class IndexJobKind
{
    public const string Record = "record";
    public const string Artifact = "artifact";
}

public sealed class IndexJobRow
{
    public Guid Id { get; set; }

    public string Kind { get; set; } = string.Empty;

    public Guid RecordId { get; set; }

    public Guid? ArtifactId { get; set; }

    public int Attempts { get; set; }
}

public static class IndexJobBackoff
{
    private static readonly TimeSpan Base = TimeSpan.FromSeconds(30);

    public static TimeSpan For(int attempts) => Base * Math.Pow(2, Math.Max(0, attempts - 1));

    // How long a job waits after the embedding service itself was unavailable.
    public static readonly TimeSpan Outage = TimeSpan.FromMinutes(1);
}

public sealed class IndexJobRepository(NpgsqlDataSource dataSource, ISqlScriptLoader sql)
{
    public const int MaxAttempts = 5;

    private const string Feature = "Search";
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

    // Used inside the saving transaction so a rolled-back save leaves no job behind.
    public static Task EnqueueAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ISqlScriptLoader scripts,
        string kind,
        Guid recordId,
        Guid? artifactId,
        CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            scripts.Get(Feature, "enqueue-index-job"),
            new { Id = Guid.CreateVersion7(), Kind = kind, RecordId = recordId, ArtifactId = artifactId },
            transaction,
            cancellationToken: cancellationToken));

    public async Task EnqueueAsync(string kind, Guid recordId, Guid? artifactId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await EnqueueAsync(connection, transaction, sql, kind, recordId, artifactId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IndexJobRow?> ClaimAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<IndexJobRow>(new CommandDefinition(
            sql.Get(Feature, "claim-index-job"), new { StaleSeconds = StaleAfter.TotalSeconds }, cancellationToken: cancellationToken));
    }

    public async Task CompleteAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            sql.Get(Feature, "complete-index-job"), new { Id = jobId }, cancellationToken: cancellationToken));
    }

    // Only the error type is stored; messages could carry content.
    public async Task DeferAsync(Guid jobId, string errorType, TimeSpan delay, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            sql.Get(Feature, "defer-index-job"),
            new { Id = jobId, DelaySeconds = delay.TotalSeconds, Error = errorType },
            cancellationToken: cancellationToken));
    }

    // Only the error type is stored; messages could carry content.
    public async Task FailAsync(Guid jobId, int attempts, string errorType, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            sql.Get(Feature, "fail-index-job"),
            new { Id = jobId, Attempts = attempts, MaxAttempts, BackoffSeconds = IndexJobBackoff.For(attempts).TotalSeconds, Error = errorType },
            cancellationToken: cancellationToken));
    }
}
