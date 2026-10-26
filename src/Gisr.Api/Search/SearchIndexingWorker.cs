using Gisr.Database.Artifacts;
using Gisr.Database.Records;
using Gisr.Database.Search;
using Gisr.Api.Search.Extraction;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Gisr.Api.Search;

// Logs only identifiers, counts and exception types: never record text, file names, or file content.
public sealed class SearchIndexingWorker(
    IndexJobRepository jobs,
    RecordRepository records,
    ArtifactRepository artifacts,
    SearchRepository search,
    IArtifactStore store,
    DocumentTextExtractor extractor,
    IEmbeddingGenerator<string, Embedding<float>> embeddings,
    IOptions<SearchOptions> options,
    ILogger<SearchIndexingWorker> logger) : BackgroundService
{
    private const int EmbeddingBatchSize = 16;

    // The indexer reads everything, including sealed records: access is enforced when results are queried.
    private static readonly RecordCaller Indexer = new(Guid.Empty, IsOfficer: true);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!SearchServiceCollectionExtensions.IndexingEnabled(options.Value))
        {
            logger.LogInformation("Search indexing is off because embeddings are not configured");
            return;
        }

        var poll = TimeSpan.FromSeconds(Math.Max(1, options.Value.Indexing.PollIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await ProcessNextAsync(stoppingToken))
                {
                    await Task.Delay(poll, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError("Search indexing loop failed: {ErrorType}", ex.GetType().Name);
                await Task.Delay(poll, stoppingToken);
            }
        }
    }

    // Returns false when no job was ready.
    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        var job = await jobs.ClaimAsync(cancellationToken);
        if (job is null)
        {
            return false;
        }

        try
        {
            if (job.Kind == IndexJobKind.Artifact && job.ArtifactId is { } artifactId)
            {
                await IndexArtifactAsync(job.RecordId, artifactId, cancellationToken);
            }
            else
            {
                await IndexRecordAsync(job.RecordId, cancellationToken);
            }

            await jobs.CompleteAsync(job.Id, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Indexing {Kind} job {JobId} for record {RecordId} failed on attempt {Attempt}: {ErrorType}",
                job.Kind, job.Id, job.RecordId, job.Attempts, EmbeddingHelper.Describe(ex));
            if (EmbeddingHelper.IsOutage(ex))
            {
                await jobs.DeferAsync(job.Id, ex.GetType().Name, IndexJobBackoff.Outage, cancellationToken);
                return true;
            }

            await jobs.FailAsync(job.Id, job.Attempts, ex.GetType().Name, cancellationToken);
            if (job.Attempts >= IndexJobRepository.MaxAttempts && job.ArtifactId is { } failedArtifact)
            {
                await artifacts.SetIndexStatusAsync(failedArtifact, "failed", null, cancellationToken);
            }
        }

        return true;
    }

    private async Task IndexRecordAsync(Guid recordId, CancellationToken cancellationToken)
    {
        var aggregate = await records.GetAsync(recordId, Indexer, cancellationToken);
        if (aggregate is null)
        {
            return;
        }

        var chunks = await ChunkAndEmbedAsync(RecordTextBuilder.Build(aggregate), cancellationToken);
        await search.ReplaceChunksAsync(recordId, null, chunks, cancellationToken);
        logger.LogInformation("Indexed record {RecordId} into {ChunkCount} chunks", recordId, chunks.Count);
    }

    private async Task IndexArtifactAsync(Guid recordId, Guid artifactId, CancellationToken cancellationToken)
    {
        var artifact = await artifacts.GetAsync(artifactId, Indexer, cancellationToken);
        if (artifact is null)
        {
            // Removed before it was indexed; nothing to keep.
            await search.ReplaceChunksAsync(recordId, artifactId, [], cancellationToken);
            return;
        }

        byte[] content;
        await using (var blob = await store.OpenReadAsync(artifact.BlobName, cancellationToken))
        {
            if (blob is null)
            {
                await FinishAsync(recordId, artifactId, ExtractionResult.Failed(), cancellationToken);
                return;
            }

            using var buffer = new MemoryStream();
            await blob.Content.CopyToAsync(buffer, cancellationToken);
            content = buffer.ToArray();
        }

        var result = await extractor.ExtractAsync(artifact.ContentType, content, cancellationToken);
        await FinishAsync(recordId, artifactId, result, cancellationToken);
    }

    private async Task FinishAsync(Guid recordId, Guid artifactId, ExtractionResult result, CancellationToken cancellationToken)
    {
        if (result.Status != ExtractionStatus.Extracted)
        {
            await search.ReplaceChunksAsync(recordId, artifactId, [], cancellationToken);
            await artifacts.SetIndexStatusAsync(artifactId, result.Status == ExtractionStatus.Skipped ? "skipped" : "failed", result.Detail, cancellationToken);
            logger.LogInformation("Artifact {ArtifactId} was not indexed: {Outcome} {Detail}", artifactId, result.Status, result.Detail);
            return;
        }

        var chunks = await ChunkAndEmbedAsync(result.Text, cancellationToken);
        await search.ReplaceChunksAsync(recordId, artifactId, chunks, cancellationToken);
        await artifacts.SetIndexStatusAsync(artifactId, "indexed", result.Detail, cancellationToken);
        logger.LogInformation("Indexed artifact {ArtifactId} into {ChunkCount} chunks", artifactId, chunks.Count);
    }

    private async Task<IReadOnlyList<ChunkWrite>> ChunkAndEmbedAsync(string text, CancellationToken cancellationToken)
    {
        var pieces = Chunker.Split(text);
        var chunks = new List<ChunkWrite>(pieces.Count);
        foreach (var batch in pieces.Chunk(EmbeddingBatchSize))
        {
            var vectors = await EmbeddingHelper.EmbedAsync(embeddings, batch, cancellationToken);
            for (var i = 0; i < batch.Length; i++)
            {
                chunks.Add(new ChunkWrite(chunks.Count, batch[i], vectors[i]));
            }
        }

        return chunks;
    }
}

public static class EmbeddingHelper
{
    // The service being unreachable, throttled, or refusing our credentials says nothing about the document, so the job just waits.
    public static bool IsOutage(Exception exception) => exception switch
    {
        HttpRequestException or TimeoutException => true,
        System.ClientModel.ClientResultException client => client.Status is 401 or 403 or 408 or 429 or >= 500,
        _ => exception.GetType().Namespace == "Azure.Identity",
    };

    // Exception messages can echo request content, so only credential failures (which cannot) are described in full.
    public static string Describe(Exception exception) => exception switch
    {
        _ when exception.GetType().Namespace == "Azure.Identity" => $"{exception.GetType().Name}: {exception.Message}",
        System.ClientModel.ClientResultException client => $"{client.GetType().Name} (HTTP {client.Status})",
        _ => exception.GetType().Name,
    };

    public static async Task<IReadOnlyList<float[]>> EmbedAsync(
        IEmbeddingGenerator<string, Embedding<float>> generator, IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        var generated = await generator.GenerateAsync(texts, null, cancellationToken);
        if (generated.Count != texts.Count || generated.Any(e => e.Vector.Length != SearchRepository.Dimensions))
        {
            throw new InvalidOperationException("The embedding service returned an unexpected result.");
        }

        return [.. generated.Select(e => e.Vector.ToArray())];
    }
}

// Re-queues scanned PDFs skipped for lack of OCR budget once a new month's budget exists.
public sealed class MonthlyRequeueService(
    OcrUsageRepository usage,
    TimeProvider clock,
    IOptions<SearchOptions> options,
    ILogger<MonthlyRequeueService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!SearchServiceCollectionExtensions.IndexingEnabled(options.Value))
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError("Monthly OCR requeue failed: {ErrorType}", ex.GetType().Name);
                await Task.Delay(Interval, stoppingToken);
            }
        }
    }

    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var requeued = await usage.RequeueBudgetSkippedAsync(new DateOnly(now.Year, now.Month, 1), cancellationToken);
        if (requeued > 0)
        {
            logger.LogInformation("Re-queued {Count} files that were skipped for the OCR budget", requeued);
        }

        return requeued;
    }
}
