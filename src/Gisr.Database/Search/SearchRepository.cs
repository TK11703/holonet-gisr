using Dapper;
using Gisr.Database.Common;
using Gisr.Database.Records;
using Npgsql;
using Pgvector;

namespace Gisr.Database.Search;

public sealed record ChunkWrite(int ChunkIndex, string Content, float[] Embedding);

public sealed class SearchHitRow
{
    public Guid RecordId { get; set; }

    public string RecordTitle { get; set; } = string.Empty;

    public string Affiliation { get; set; } = string.Empty;

    public string SubjectDisplayName { get; set; } = string.Empty;

    public bool IsSealed { get; set; }

    public string SourceType { get; set; } = string.Empty;

    public Guid? ArtifactId { get; set; }

    public string? ArtifactFileName { get; set; }

    public string Content { get; set; } = string.Empty;

    public double Distance { get; set; }
}

public sealed class SearchRepository(NpgsqlDataSource dataSource, ISqlScriptLoader sql)
{
    public const int Dimensions = 1536;

    private const string Feature = "Search";

    // Access scoping happens in SQL before ranking, so restricted chunks are never returned or counted.
    public async Task<PagedRows<SearchHitRow>> SearchAsync(
        RecordCaller caller,
        RecordListScope scope,
        float[] queryEmbedding,
        string? affiliation,
        double maxDistance,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(Command("set-search-options", null, transaction, cancellationToken));

        var filter = new
        {
            CallerAccountId = caller.AccountId,
            caller.IsOfficer,
            AllRecords = scope == RecordListScope.All,
            Affiliation = affiliation,
            Embedding = new Vector(queryEmbedding),
            MaxDistance = maxDistance,
        };

        var rows = await connection.QueryAsync<SearchHitRow>(Command("search-chunks", new
        {
            filter.CallerAccountId,
            filter.IsOfficer,
            filter.AllRecords,
            filter.Affiliation,
            filter.Embedding,
            filter.MaxDistance,
            PageSize = pageSize,
            Offset = (page - 1) * pageSize,
        }, transaction, cancellationToken));
        var total = await connection.ExecuteScalarAsync<long>(Command("count-search-hits", filter, transaction, cancellationToken));
        await transaction.CommitAsync(cancellationToken);

        return new PagedRows<SearchHitRow>([.. rows], total);
    }

    // A null artifact id replaces the record's own chunks; otherwise the chunks of that artifact.
    public async Task ReplaceChunksAsync(Guid recordId, Guid? artifactId, IReadOnlyList<ChunkWrite> chunks, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await connection.ExecuteAsync(Command("delete-chunks", new { RecordId = recordId, ArtifactId = artifactId }, transaction, cancellationToken));
        if (chunks.Count > 0)
        {
            await connection.ExecuteAsync(Command("insert-chunk", chunks.Select(chunk => new
            {
                Id = Guid.CreateVersion7(),
                RecordId = recordId,
                SourceType = artifactId is null ? "record" : "artifact",
                ArtifactId = artifactId,
                chunk.ChunkIndex,
                chunk.Content,
                Embedding = new Vector(chunk.Embedding),
            }).ToList(), transaction, cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private CommandDefinition Command(string script, object? parameters, NpgsqlTransaction transaction, CancellationToken cancellationToken) =>
        new(sql.Get(Feature, script), parameters, transaction, cancellationToken: cancellationToken);
}
