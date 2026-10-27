using Dapper;
using Gisr.Database.Common;
using Gisr.Database.Notifications;
using Gisr.Database.Search;
using Npgsql;

namespace Gisr.Database.Records;

public sealed class RecordRepository(NpgsqlDataSource dataSource, ISqlScriptLoader sql)
{
    private const string Feature = "Records";

    public Task<RecordAggregate> CreateAsync(
        Guid subjectAccountId,
        Guid authorAccountId,
        RecordWrite write,
        CancellationToken cancellationToken) =>
        CreateAsync(subjectAccountId, authorAccountId, write, null, cancellationToken);

    public async Task<RecordAggregate> CreateAsync(
        Guid subjectAccountId,
        Guid authorAccountId,
        RecordWrite write,
        NotificationWrite? notification,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var recordId = Guid.CreateVersion7();
        await connection.ExecuteAsync(Command("insert-record", new
        {
            Id = recordId,
            SubjectAccountId = subjectAccountId,
            AuthorAccountId = authorAccountId,
            write.Title,
            write.Affiliation,
            write.EngagementStart,
            write.EngagementEnd,
            write.EngagementDetail,
            write.IsSealed,
            write.OverrideCodeHash,
        }, transaction, cancellationToken));

        await InsertCollectionsAsync(connection, transaction, recordId, write, cancellationToken);
        await InsertRevisionAsync(connection, transaction, recordId, authorAccountId, cancellationToken);
        await IndexJobRepository.EnqueueAsync(connection, transaction, sql, IndexJobKind.Record, recordId, null, cancellationToken);

        if (notification is not null)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                sql.Get("Notifications", "insert-notification"),
                new
                {
                    Id = Guid.CreateVersion7(),
                    notification.RecipientAccountId,
                    notification.Kind,
                    RecordId = recordId,
                    notification.Message,
                },
                transaction,
                cancellationToken: cancellationToken));
        }

        // Read back as unlocked: the author just wrote this content, so the sealed-record redaction must not apply.
        var aggregate = await LoadAsync(connection, transaction, recordId, new RecordCaller(subjectAccountId, false, true), cancellationToken)
            ?? throw new InvalidOperationException("The record that was just created could not be read back.");
        await transaction.CommitAsync(cancellationToken);
        return aggregate;
    }

    public async Task<string?> GetOverrideCodeHashAsync(Guid recordId, Guid subjectAccountId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<string?>(Command(
            "get-override-code-hash", new { Id = recordId, SubjectAccountId = subjectAccountId }, null, cancellationToken));
    }

    public async Task<RecordAggregate?> GetAsync(Guid recordId, RecordCaller caller, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await LoadAsync(connection, null, recordId, caller, cancellationToken);
    }

    public async Task<RecordUpdateResult> UpdateAsync(
        Guid recordId,
        RecordCaller caller,
        int expectedVersion,
        Guid updatedByAccountId,
        RecordWrite write,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var affected = await connection.ExecuteAsync(Command("update-record", new
        {
            Id = recordId,
            CallerAccountId = caller.AccountId,
            caller.IsOfficer,
            ExpectedVersion = expectedVersion,
            UpdatedByAccountId = updatedByAccountId,
            write.Title,
            write.Affiliation,
            write.EngagementStart,
            write.EngagementEnd,
            write.EngagementDetail,
            write.IsSealed,
            write.OverrideCodeHash,
        }, transaction, cancellationToken));

        if (affected == 0)
        {
            var exists = await LoadAsync(connection, transaction, recordId, caller, cancellationToken) is not null;
            return new RecordUpdateResult(exists ? RecordUpdateOutcome.Conflict : RecordUpdateOutcome.NotFound, null);
        }

        await connection.ExecuteAsync(Command("delete-incidents", new { RecordId = recordId }, transaction, cancellationToken));
        await connection.ExecuteAsync(Command("delete-awards", new { RecordId = recordId }, transaction, cancellationToken));

        // Unsealing or replacing the code must end every viewing in progress, in the same transaction.
        if (!write.IsSealed || write.OverrideCodeHash is not null)
        {
            await connection.ExecuteAsync(Command("delete-view-grants-for-record", new { RecordId = recordId }, transaction, cancellationToken));
        }

        await InsertCollectionsAsync(connection, transaction, recordId, write, cancellationToken);
        await InsertRevisionAsync(connection, transaction, recordId, updatedByAccountId, cancellationToken);
        await IndexJobRepository.EnqueueAsync(connection, transaction, sql, IndexJobKind.Record, recordId, null, cancellationToken);

        var aggregate = await LoadAsync(connection, transaction, recordId, caller, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new RecordUpdateResult(RecordUpdateOutcome.Updated, aggregate);
    }

    // Soft delete: the row, its revisions, and its access log stay for audit; every read path filters deleted records out.
    public async Task<bool> DeleteAsync(Guid recordId, RecordCaller caller, Guid deletedByAccountId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var affected = await connection.ExecuteAsync(Command("delete-record", new
        {
            Id = recordId,
            CallerAccountId = caller.AccountId,
            caller.IsOfficer,
            DeletedByAccountId = deletedByAccountId,
        }, transaction, cancellationToken));
        if (affected == 0)
        {
            return false;
        }

        await connection.ExecuteAsync(Command("delete-view-grants-for-record", new { RecordId = recordId }, transaction, cancellationToken));
        await connection.ExecuteAsync(Command("delete-record-chunks", new { RecordId = recordId }, transaction, cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<PagedRows<RecordRow>> ListAsync(
        RecordCaller caller,
        RecordListScope scope,
        Guid? subjectAccountId,
        string? affiliation,
        RecordSort sort,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var filter = new
        {
            CallerAccountId = caller.AccountId,
            caller.IsOfficer,
            AllRecords = scope == RecordListScope.All,
            SubjectAccountId = subjectAccountId,
            Affiliation = affiliation,
        };

        var rows = await connection.QueryAsync<RecordRow>(Command("list-records", new
        {
            filter.CallerAccountId,
            filter.IsOfficer,
            filter.AllRecords,
            filter.SubjectAccountId,
            filter.Affiliation,
            Sort = SortKey(sort),
            PageSize = pageSize,
            Offset = (page - 1) * pageSize,
        }, null, cancellationToken));
        var total = await connection.ExecuteScalarAsync<long>(Command("count-records", filter, null, cancellationToken));

        return new PagedRows<RecordRow>([.. rows], total);
    }

    private static string SortKey(RecordSort sort) => sort switch
    {
        RecordSort.UpdatedAsc => "updatedAsc",
        RecordSort.TitleAsc => "titleAsc",
        RecordSort.TitleDesc => "titleDesc",
        _ => "updatedDesc",
    };

    private CommandDefinition Command(string script, object parameters, NpgsqlTransaction? transaction, CancellationToken cancellationToken) =>
        new(sql.Get(Feature, script), parameters, transaction, cancellationToken: cancellationToken);

    private async Task<RecordAggregate?> LoadAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid recordId,
        RecordCaller caller,
        CancellationToken cancellationToken)
    {
        var record = await connection.QuerySingleOrDefaultAsync<RecordRow>(Command(
            "get-record", new { Id = recordId, CallerAccountId = caller.AccountId, caller.IsOfficer, caller.Unlocked }, transaction, cancellationToken));
        if (record is null)
        {
            return null;
        }

        if (record.IsLocked)
        {
            return new RecordAggregate(record, [], []);
        }

        var incidents = await connection.QueryAsync<IncidentRow>(Command("list-incidents", new { RecordId = recordId }, transaction, cancellationToken));
        var awards = await connection.QueryAsync<AwardRow>(Command("list-awards", new { RecordId = recordId }, transaction, cancellationToken));
        return new RecordAggregate(record, [.. incidents], [.. awards]);
    }

    private async Task InsertCollectionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid recordId,
        RecordWrite write,
        CancellationToken cancellationToken)
    {
        var incidents = write.Incidents.Select((incident, index) => new
        {
            Id = Guid.CreateVersion7(),
            RecordId = recordId,
            incident.OccurredOn,
            incident.Title,
            incident.Description,
            SortOrder = index,
        }).ToList();
        if (incidents.Count > 0)
        {
            await connection.ExecuteAsync(Command("insert-incident", incidents, transaction, cancellationToken));
        }

        var awards = write.Awards.Select((award, index) => new
        {
            Id = Guid.CreateVersion7(),
            RecordId = recordId,
            award.Name,
            award.AwardedOn,
            award.Issuer,
            award.Description,
            SortOrder = index,
        }).ToList();
        if (awards.Count > 0)
        {
            await connection.ExecuteAsync(Command("insert-award", awards, transaction, cancellationToken));
        }
    }

    private async Task InsertRevisionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid recordId,
        Guid changedByAccountId,
        CancellationToken cancellationToken) =>
        await connection.ExecuteAsync(Command("insert-revision", new
        {
            Id = Guid.CreateVersion7(),
            RecordId = recordId,
            ChangedByAccountId = changedByAccountId,
        }, transaction, cancellationToken));
}
