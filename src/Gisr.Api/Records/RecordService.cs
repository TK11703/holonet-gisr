using Gisr.Api.Accounts;
using Gisr.Api.Common;
using Gisr.Api.Notifications;
using Gisr.Database.Records;
using Gisr.Shared.Common;
using Gisr.Shared.Records;

namespace Gisr.Api.Records;

public sealed class RecordService(
    RecordRepository records,
    RevisionRepository revisions,
    AccessLogRepository accessLog,
    AccountService accounts,
    ICurrentUser user)
{
    private const int DefaultPageSize = 20;

    public async Task<RecordDto> CreateAsync(CreateRecordRequest request, CancellationToken cancellationToken)
    {
        var author = await accounts.EnsureAccountAsync(cancellationToken);
        var caller = new RecordCaller(author.Id, user.IsOfficer);
        var forSelf = request.SubjectObjectId is null || request.SubjectObjectId == user.ObjectId;
        if (RecordAccessPolicy.Create(forSelf, caller) != AccessDecision.Allowed)
        {
            throw new ProblemException(StatusCodes.Status403Forbidden, "Forbidden", "You can only create records for yourself.");
        }

        if (request.IsSealed && forSelf)
        {
            throw new ValidationProblemException("isSealed", "Only a service record written about another citizen can be sealed.");
        }

        var subject = forSelf ? author : await accounts.ResolveSubjectAsync(request.SubjectObjectId!.Value, cancellationToken);
        var notification = forSelf ? null : NotificationService.ForServiceRecord(subject, author, request.IsSealed);
        var write = RecordMapper.ToWrite(request) with
        {
            IsSealed = request.IsSealed,
            OverrideCodeHash = request.IsSealed ? OverrideCodeHasher.Hash(request.OverrideCode!) : null,
        };

        var created = await records.CreateAsync(subject.Id, caller.AccountId, write, notification, cancellationToken);
        return RecordMapper.ToDto(created, caller);
    }

    public async Task<RecordDto> GetAsync(Guid recordId, CancellationToken cancellationToken)
    {
        var caller = await CallerAsync(cancellationToken);
        var aggregate = await FindViewableAsync(recordId, caller, lockedAsNotFound: false, cancellationToken);

        if (RecordAccessPolicy.RequiresAccessLog(aggregate.Record, caller))
        {
            await accessLog.InsertAsync(recordId, caller.AccountId, AccessLogKind.Record, cancellationToken);
        }

        return RecordMapper.ToDto(aggregate, caller);
    }

    public async Task<RecordDto> UpdateAsync(Guid recordId, UpdateRecordRequest request, CancellationToken cancellationToken)
    {
        var caller = await CallerAsync(cancellationToken);
        var existing = await FindViewableAsync(recordId, caller, lockedAsNotFound: false, cancellationToken);
        if (RecordAccessPolicy.Edit(existing.Record, caller) != AccessDecision.Allowed)
        {
            throw new ProblemException(StatusCodes.Status403Forbidden, "Forbidden", "You are not allowed to edit this record.");
        }

        var write = RecordMapper.ToWrite(request) with { IsSealed = request.IsSealed, OverrideCodeHash = SealedCodeHash(existing.Record, request) };

        var result = await records.UpdateAsync(
            recordId, caller, request.ExpectedVersion, caller.AccountId, write, cancellationToken);

        return result.Outcome switch
        {
            RecordUpdateOutcome.Updated => RecordMapper.ToDto(result.Record!, caller),
            RecordUpdateOutcome.Conflict => throw new ConflictException("The record was changed since it was loaded. Reload it and try again."),
            _ => throw NotFound(),
        };
    }

    public async Task<PagedResult<RecordSummaryDto>> ListAsync(RecordListQuery query, CancellationToken cancellationToken)
    {
        var caller = await CallerAsync(cancellationToken);
        var scope = string.Equals(query.Scope, "all", StringComparison.Ordinal) ? RecordListScope.All : RecordListScope.Mine;
        if (scope == RecordListScope.All && RecordAccessPolicy.ListAll(caller) != AccessDecision.Allowed)
        {
            throw new ProblemException(StatusCodes.Status403Forbidden, "Forbidden", "You are not allowed to list all records.");
        }

        var page = query.Page ?? 1;
        var pageSize = query.PageSize ?? DefaultPageSize;
        var sort = query.Sort switch
        {
            "updatedAsc" => RecordSort.UpdatedAsc,
            "titleAsc" => RecordSort.TitleAsc,
            "titleDesc" => RecordSort.TitleDesc,
            _ => RecordSort.UpdatedDesc,
        };

        var rows = await records.ListAsync(caller, scope, query.SubjectAccountId, query.Affiliation, sort, page, pageSize, cancellationToken);
        return new PagedResult<RecordSummaryDto>(page, pageSize, rows.TotalCount, [.. rows.Items.Select(RecordMapper.ToSummary)]);
    }

    public async Task<PagedResult<RevisionDto>> HistoryAsync(Guid recordId, HistoryQuery query, CancellationToken cancellationToken)
    {
        var caller = await CallerAsync(cancellationToken);
        await FindViewableAsync(recordId, caller, lockedAsNotFound: true, cancellationToken);

        var page = query.Page ?? 1;
        var pageSize = query.PageSize ?? DefaultPageSize;
        var rows = await revisions.ListAsync(recordId, page, pageSize, cancellationToken);
        return new PagedResult<RevisionDto>(page, pageSize, rows.TotalCount, [.. rows.Items.Select(RecordMapper.ToRevisionDto)]);
    }

    // Sealing needs server-side state: only service records can be sealed, and an already sealed record keeps its code unless a new one is sent.
    private static string? SealedCodeHash(RecordRow existing, UpdateRecordRequest request)
    {
        if (!request.IsSealed)
        {
            return null;
        }

        if (existing.AuthorAccountId == existing.SubjectAccountId)
        {
            throw new ValidationProblemException("isSealed", "Only a service record written about another citizen can be sealed.");
        }

        if (request.OverrideCode is null)
        {
            return existing.IsSealed
                ? null
                : throw new ValidationProblemException("overrideCode", "An override code is required to seal a record.");
        }

        return OverrideCodeHasher.Hash(request.OverrideCode);
    }

    private async Task<RecordCaller> CallerAsync(CancellationToken cancellationToken)
    {
        var account = await accounts.EnsureAccountAsync(cancellationToken);
        return new RecordCaller(account.Id, user.IsOfficer);
    }

    private async Task<RecordAggregate> FindViewableAsync(Guid recordId, RecordCaller caller, bool lockedAsNotFound, CancellationToken cancellationToken)
    {
        var aggregate = await records.GetAsync(recordId, caller, cancellationToken) ?? throw NotFound();
        if (RecordAccessPolicy.View(aggregate.Record, caller) != AccessDecision.Allowed)
        {
            throw NotFound();
        }

        if (aggregate.Record.IsLocked)
        {
            throw lockedAsNotFound ? NotFound() : Sealed();
        }

        return aggregate;
    }

    private static ProblemException NotFound() => new(StatusCodes.Status404NotFound, "Not Found");

    private static ProblemException Sealed() => new(
        StatusCodes.Status403Forbidden,
        "Forbidden",
        "This record is sealed.",
        new Dictionary<string, object?> { ["sealed"] = true });
}
