using Dapper;
using Gisr.Database.Common;
using Gisr.Database.Records;
using Gisr.Database.Search;
using Npgsql;

namespace Gisr.Database.Artifacts;

public sealed class ArtifactRepository(NpgsqlDataSource dataSource, ISqlScriptLoader sql)
{
    public const int MaxActivePerRecord = 50;

    private const string Feature = "Artifacts";

    // Returns null when the record already has the maximum number of active artifacts.
    public async Task<ArtifactRow?> InsertAsync(ArtifactWrite write, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Serialise concurrent uploads to one record so the cap holds under races.
        await connection.ExecuteAsync(Command("lock-record", new { write.RecordId }, transaction, cancellationToken));
        var inserted = await connection.ExecuteAsync(Command("insert-artifact", new
        {
            write.Id,
            write.RecordId,
            write.FileName,
            write.ContentType,
            write.SizeBytes,
            write.Sha256,
            write.BlobName,
            write.ThumbnailBlobName,
            write.IsImage,
            write.Description,
            write.UploadedByAccountId,
            MaxActive = MaxActivePerRecord,
        }, transaction, cancellationToken));
        if (inserted == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var row = await connection.QuerySingleAsync<ArtifactRow>(Command(
            "get-artifact", new { write.Id, CallerAccountId = write.UploadedByAccountId, IsOfficer = true, Unlocked = true }, transaction, cancellationToken));
        if (!write.IsImage)
        {
            await IndexJobRepository.EnqueueAsync(connection, transaction, sql, IndexJobKind.Artifact, write.RecordId, write.Id, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return row;
    }

    public async Task<PagedRows<ArtifactRow>> ListAsync(
        Guid recordId,
        RecordCaller caller,
        bool imagesOnly,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var scope = new { RecordId = recordId, CallerAccountId = caller.AccountId, caller.IsOfficer, caller.Unlocked, ImagesOnly = imagesOnly };

        var rows = await connection.QueryAsync<ArtifactRow>(Command("list-artifacts", new
        {
            scope.RecordId,
            scope.CallerAccountId,
            scope.IsOfficer,
            scope.Unlocked,
            scope.ImagesOnly,
            PageSize = pageSize,
            Offset = (page - 1) * pageSize,
        }, null, cancellationToken));
        var total = await connection.ExecuteScalarAsync<long>(Command("count-artifacts", scope, null, cancellationToken));
        return new PagedRows<ArtifactRow>([.. rows], total);
    }

    // A row for a locked sealed record comes back redacted (IsLocked) so the caller can ask for a view token.
    public async Task<ArtifactRow?> GetAsync(Guid artifactId, RecordCaller caller, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<ArtifactRow>(Command(
            "get-artifact", new { Id = artifactId, CallerAccountId = caller.AccountId, caller.IsOfficer, caller.Unlocked }, null, cancellationToken));
    }

    public async Task<ArtifactRow?> UpdateDescriptionAsync(Guid artifactId, string description, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<ArtifactRow>(Command(
            "update-artifact-description", new { Id = artifactId, Description = description }, null, cancellationToken));
    }

    public async Task<bool> RemoveAsync(Guid artifactId, Guid removedByAccountId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var removed = await connection.ExecuteAsync(Command(
            "soft-delete-artifact", new { Id = artifactId, RemovedByAccountId = removedByAccountId }, transaction, cancellationToken)) > 0;
        if (removed)
        {
            // A removed artifact must leave search immediately.
            await connection.ExecuteAsync(new CommandDefinition(
                sql.Get("Search", "delete-artifact-chunks"), new { ArtifactId = artifactId }, transaction, cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return removed;
    }

    // `detail` is a reason code or partial-read note; never file content.
    public async Task SetIndexStatusAsync(Guid artifactId, string status, string? detail, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(Command(
            "update-artifact-index-status", new { Id = artifactId, Status = status, Detail = detail }, null, cancellationToken));
    }

    public async Task<int> CountActiveAsync(Guid recordId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return (int)await connection.ExecuteScalarAsync<long>(Command("count-active-artifacts", new { RecordId = recordId }, null, cancellationToken));
    }

    private CommandDefinition Command(string script, object parameters, NpgsqlTransaction? transaction, CancellationToken cancellationToken) =>
        new(sql.Get(Feature, script), parameters, transaction, cancellationToken: cancellationToken);
}
