using System.Security.Cryptography;
using FluentValidation;
using Gisr.Api.Accounts;
using Gisr.Api.Common;
using Gisr.Api.Records;
using Gisr.Database.Artifacts;
using Gisr.Database.Records;
using Gisr.Shared.Artifacts;
using Gisr.Shared.Common;

namespace Gisr.Api.Artifacts;

public sealed record ArtifactUploadContent(string? FileName, long Length, Stream Content, string? Description);

public sealed record ArtifactDownload(ArtifactBlob Blob, string FileName, string ContentType, bool IsImage);

public sealed class ArtifactService(
    ArtifactRepository artifacts,
    RecordRepository records,
    AccessLogRepository accessLog,
    OverrideService overrides,
    AccountService accounts,
    IArtifactStore store,
    IValidator<ArtifactUpload> uploadValidator,
    ICurrentUser user)
{
    private const int DefaultPageSize = 20;

    public async Task<ArtifactDto> UploadAsync(Guid recordId, ArtifactUploadContent upload, CancellationToken cancellationToken)
    {
        var caller = await CallerAsync(cancellationToken);
        await RequireEditableRecordAsync(recordId, caller, cancellationToken);

        if (upload.Length > ArtifactLimits.MaxBytes)
        {
            throw TooLarge();
        }

        var bytes = await ReadAsync(upload.Content, cancellationToken);
        if (bytes.Length == 0)
        {
            throw new ValidationProblemException("file", "The file is empty.");
        }

        var fileName = ArtifactFileName.Sanitize(upload.FileName);
        var sniffed = FileTypeSniffer.Sniff(bytes, fileName)
            ?? throw new ProblemException(
                StatusCodes.Status415UnsupportedMediaType,
                "Unsupported Media Type",
                "This file type is not allowed. Upload a JPEG, PNG, WebP, GIF, PDF, DOCX, TXT, or Markdown file whose content matches its extension.");

        var description = string.IsNullOrWhiteSpace(upload.Description) ? null : upload.Description.Trim();
        var active = await artifacts.CountActiveAsync(recordId, cancellationToken);
        var validation = await uploadValidator.ValidateAsync(new ArtifactUpload(fileName, bytes.Length, sniffed.IsImage, description, active), cancellationToken);
        if (!validation.IsValid)
        {
            var error = validation.Errors[0];
            throw new ValidationProblemException(
                error.PropertyName == nameof(ArtifactUpload.Description) ? "description" : "file",
                error.ErrorMessage);
        }

        byte[]? thumbnail = null;
        if (sniffed.IsImage)
        {
            thumbnail = ThumbnailGenerator.Create(bytes)
                ?? throw new ValidationProblemException("file", "The image could not be read.");
        }

        var id = Guid.CreateVersion7();
        var blobName = $"{recordId:N}/{id:N}";
        var thumbnailBlobName = thumbnail is null ? null : blobName + ".thumb.jpg";
        var write = new ArtifactWrite(
            id, recordId, fileName, sniffed.ContentType, bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)),
            blobName, thumbnailBlobName, sniffed.IsImage, description, caller.AccountId);

        ArtifactRow? row;
        try
        {
            await store.SaveAsync(blobName, new MemoryStream(bytes, writable: false), sniffed.ContentType, cancellationToken);
            if (thumbnail is not null)
            {
                await store.SaveAsync(thumbnailBlobName!, new MemoryStream(thumbnail, writable: false), "image/jpeg", cancellationToken);
            }

            row = await artifacts.InsertAsync(write, cancellationToken);
        }
        catch
        {
            await DiscardBlobsAsync(blobName, thumbnailBlobName);
            throw;
        }

        if (row is null)
        {
            await DiscardBlobsAsync(blobName, thumbnailBlobName);
            throw new ValidationProblemException("file", $"Max {ArtifactLimits.MaxActivePerRecord} active artifacts per record.");
        }

        return ArtifactMapper.ToDto(row);
    }

    public async Task<PagedResult<ArtifactDto>> ListAsync(Guid recordId, string? viewToken, ArtifactListQuery query, CancellationToken cancellationToken)
    {
        var (caller, record) = await OpenReadableRecordAsync(recordId, viewToken, cancellationToken);
        var page = query.Page ?? 1;
        var pageSize = query.PageSize ?? DefaultPageSize;

        var rows = await artifacts.ListAsync(recordId, caller, string.Equals(query.Kind, "image", StringComparison.Ordinal), page, pageSize, cancellationToken);
        if (RecordAccessPolicy.RequiresAccessLog(record, caller))
        {
            await accessLog.InsertAsync(recordId, caller.AccountId, AccessLogKind.Artifact, cancellationToken);
        }

        return new PagedResult<ArtifactDto>(page, pageSize, rows.TotalCount, [.. rows.Items.Select(ArtifactMapper.ToDto)]);
    }

    public async Task<ArtifactDownload> OpenAsync(Guid artifactId, string? viewToken, bool thumbnail, CancellationToken cancellationToken)
    {
        var caller = await CallerAsync(cancellationToken);
        var row = await artifacts.GetAsync(artifactId, caller, cancellationToken) ?? throw NotFound();
        var (readCaller, record) = await OpenReadableRecordAsync(row.RecordId, viewToken, cancellationToken);
        if (row.IsLocked)
        {
            row = await artifacts.GetAsync(artifactId, readCaller, cancellationToken) ?? throw NotFound();
        }

        var blobName = thumbnail ? row.ThumbnailBlobName : row.BlobName;
        if (string.IsNullOrEmpty(blobName))
        {
            throw NotFound();
        }

        if (!thumbnail && RecordAccessPolicy.RequiresAccessLog(record, readCaller))
        {
            await accessLog.InsertAsync(row.RecordId, readCaller.AccountId, AccessLogKind.Artifact, cancellationToken);
        }

        var blob = await store.OpenReadAsync(blobName, cancellationToken) ?? throw NotFound();
        return new ArtifactDownload(blob, row.FileName, thumbnail ? FileTypeSniffer.Jpeg : row.ContentType, row.IsImage);
    }

    public async Task<ArtifactDto> UpdateDescriptionAsync(Guid artifactId, UpdateArtifactRequest request, CancellationToken cancellationToken)
    {
        await RequireEditableArtifactAsync(artifactId, cancellationToken);
        var updated = await artifacts.UpdateDescriptionAsync(artifactId, request.Description.Trim(), cancellationToken) ?? throw NotFound();
        return ArtifactMapper.ToDto(updated);
    }

    public async Task RemoveAsync(Guid artifactId, CancellationToken cancellationToken)
    {
        var (caller, _) = await RequireEditableArtifactAsync(artifactId, cancellationToken);
        if (!await artifacts.RemoveAsync(artifactId, caller.AccountId, cancellationToken))
        {
            throw NotFound();
        }
    }

    private async Task<RecordCaller> CallerAsync(CancellationToken cancellationToken)
    {
        var account = await accounts.EnsureAccountAsync(cancellationToken);
        return new RecordCaller(account.Id, user.IsOfficer);
    }

    // A locked sealed record is only readable with a view token that belongs to this caller and this record.
    private async Task<(RecordCaller Caller, RecordRow Record)> OpenReadableRecordAsync(Guid recordId, string? viewToken, CancellationToken cancellationToken)
    {
        var caller = await CallerAsync(cancellationToken);
        var aggregate = await records.GetAsync(recordId, caller, cancellationToken) ?? throw NotFound();
        if (RecordAccessPolicy.View(aggregate.Record, caller) != AccessDecision.Allowed)
        {
            throw NotFound();
        }

        if (!aggregate.Record.IsLocked)
        {
            return (caller, aggregate.Record);
        }

        if (!await overrides.IsValidAsync(recordId, viewToken, cancellationToken))
        {
            throw NotFound();
        }

        return (caller with { Unlocked = true }, aggregate.Record);
    }

    // Changes never accept a view token: a locked record hides its artifacts, and the subject of a service record cannot edit them.
    private async Task<RecordRow> RequireEditableRecordAsync(Guid recordId, RecordCaller caller, CancellationToken cancellationToken)
    {
        var aggregate = await records.GetAsync(recordId, caller, cancellationToken) ?? throw NotFound();
        if (RecordAccessPolicy.View(aggregate.Record, caller) != AccessDecision.Allowed || aggregate.Record.IsLocked)
        {
            throw NotFound();
        }

        return RecordAccessPolicy.Edit(aggregate.Record, caller) == AccessDecision.Allowed
            ? aggregate.Record
            : throw new ProblemException(StatusCodes.Status403Forbidden, "Forbidden", "You are not allowed to change this record's artifacts.");
    }

    private async Task<(RecordCaller Caller, ArtifactRow Artifact)> RequireEditableArtifactAsync(Guid artifactId, CancellationToken cancellationToken)
    {
        var caller = await CallerAsync(cancellationToken);
        var artifact = await artifacts.GetAsync(artifactId, caller, cancellationToken) ?? throw NotFound();
        if (artifact.IsLocked)
        {
            throw NotFound();
        }

        await RequireEditableRecordAsync(artifact.RecordId, caller, cancellationToken);
        return (caller, artifact);
    }

    private static async Task<byte[]> ReadAsync(Stream content, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81_920];
        int read;
        while ((read = await content.ReadAsync(chunk, cancellationToken)) > 0)
        {
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
            if (buffer.Length > ArtifactLimits.MaxBytes)
            {
                throw TooLarge();
            }
        }

        return buffer.ToArray();
    }

    private async Task DiscardBlobsAsync(string blobName, string? thumbnailBlobName)
    {
        await store.DeleteAsync(blobName, CancellationToken.None);
        if (thumbnailBlobName is not null)
        {
            await store.DeleteAsync(thumbnailBlobName, CancellationToken.None);
        }
    }

    private static ProblemException TooLarge() => new(StatusCodes.Status413PayloadTooLarge, "Payload Too Large", "Files can be at most 25 MB.");

    private static ProblemException NotFound() => new(StatusCodes.Status404NotFound, "Not Found");
}
