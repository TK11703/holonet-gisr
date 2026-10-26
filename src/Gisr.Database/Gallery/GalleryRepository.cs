using Dapper;
using Gisr.Database.Common;
using Gisr.Database.Records;
using Npgsql;

namespace Gisr.Database.Gallery;

public sealed class GalleryGroupRow
{
    public Guid RecordId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Affiliation { get; set; } = string.Empty;

    public string SubjectDisplayName { get; set; } = string.Empty;

    public int ImageCount { get; set; }

    public Guid[] CoverArtifactIds { get; set; } = [];
}

public sealed class GalleryRepository(NpgsqlDataSource dataSource, ISqlScriptLoader sql)
{
    private const string Feature = "Gallery";

    public async Task<PagedRows<GalleryGroupRow>> ListGroupsAsync(
        RecordCaller caller,
        RecordListScope scope,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var filter = new { CallerAccountId = caller.AccountId, caller.IsOfficer, AllRecords = scope == RecordListScope.All };

        var rows = await connection.QueryAsync<GalleryGroupRow>(new CommandDefinition(
            sql.Get(Feature, "list-gallery-groups"),
            new { filter.CallerAccountId, filter.IsOfficer, filter.AllRecords, PageSize = pageSize, Offset = (page - 1) * pageSize },
            cancellationToken: cancellationToken));
        var total = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            sql.Get(Feature, "count-gallery-groups"), filter, cancellationToken: cancellationToken));

        return new PagedRows<GalleryGroupRow>([.. rows], total);
    }
}
