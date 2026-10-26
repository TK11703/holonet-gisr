using Dapper;
using Gisr.Database.Common;
using Npgsql;

namespace Gisr.Database.Records;

public sealed class RevisionRepository(NpgsqlDataSource dataSource, ISqlScriptLoader sql)
{
    public async Task<PagedRows<RevisionRow>> ListAsync(Guid recordId, int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<RevisionRow>(new CommandDefinition(
            sql.Get("Records", "list-revisions"),
            new { RecordId = recordId, PageSize = pageSize, Offset = (page - 1) * pageSize },
            cancellationToken: cancellationToken));
        var total = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            sql.Get("Records", "count-revisions"), new { RecordId = recordId }, cancellationToken: cancellationToken));

        return new PagedRows<RevisionRow>([.. rows], total);
    }
}
