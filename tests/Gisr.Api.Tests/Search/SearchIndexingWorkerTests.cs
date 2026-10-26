using System.Net;
using System.Net.Http.Json;
using Gisr.Api.Search;
using Gisr.Api.Tests.Artifacts;
using Gisr.Api.Tests.Common;
using Gisr.Database.Search;
using Gisr.Shared.Records;
using Microsoft.Extensions.DependencyInjection;

namespace Gisr.Api.Tests.Search;

[Collection("api")]
public class SearchIndexingWorkerTests(PostgresFixture postgres, AzuriteFixture azurite)
{
    private Task<ApiFactory> CreateFactoryAsync(Action<SearchOptions>? configure = null) => CreateCoreAsync(configure);

    private async Task<ApiFactory> CreateCoreAsync(Action<SearchOptions>? configure)
    {
        var factory = await ApiFactory.CreateAsync(postgres, azurite);
        factory.SearchOptionsOverride = configure;
        return factory;
    }

    private static SearchIndexingWorker Worker(ApiFactory factory) => factory.Services.GetRequiredService<SearchIndexingWorker>();

    [Fact]
    public async Task ARecord_IsChunkedEmbeddedAndStored_ForSearch()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        var record = await SearchTestData.CreateRecordAsync(client, "Temple guard", "Guarded the ancient temple of the Jedi order");

        Assert.True(await Worker(factory).ProcessNextAsync(CancellationToken.None));

        Assert.Equal(1L, await factory.ScalarAsync<long>($"SELECT count(*) FROM search_chunk WHERE record_id = '{record.Id}' AND source_type = 'record'"));
        var content = await factory.ScalarAsync<string>($"SELECT content FROM search_chunk WHERE record_id = '{record.Id}'");
        Assert.Contains("Temple guard", content, StringComparison.Ordinal);
        Assert.Contains("ancient temple", content, StringComparison.Ordinal);
        Assert.Equal("succeeded", await factory.ScalarAsync<string>("SELECT status FROM search_index_job"));
        Assert.False(await Worker(factory).ProcessNextAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ReindexingARecord_ReplacesItsPreviousChunks()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        var record = await SearchTestData.CreateRecordAsync(client, "Old title", "old summary words");
        await SearchTestData.IndexAllAsync(factory);

        var update = SealedTestData_Update(record, "New title", "brand new summary");
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/records/{record.Id}", update)).StatusCode);
        await SearchTestData.IndexAllAsync(factory);

        Assert.Equal(1L, await factory.ScalarAsync<long>($"SELECT count(*) FROM search_chunk WHERE record_id = '{record.Id}'"));
        var content = await factory.ScalarAsync<string>($"SELECT content FROM search_chunk WHERE record_id = '{record.Id}'");
        Assert.Contains("New title", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Old title", content, StringComparison.Ordinal);
    }

    private static UpdateRecordRequest SealedTestData_Update(RecordDto current, string title, string summary) =>
        new(title, current.Affiliation, current.EngagementStart, current.EngagementEnd,
            new Dictionary<string, string> { ["summary"] = summary, ["branch"] = "Navy", ["rank"] = "Admiral" },
            current.Incidents, current.Awards, current.Version);

    [Fact]
    public async Task ALongRecord_ProducesSeveralChunks_EmbeddedInBatches()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        var incidents = Enumerable.Range(0, 8)
            .Select(i => new IncidentDto(null, new DateOnly(2020, 1, 1), $"Incident {i}", string.Join(' ', Enumerable.Range(0, 300).Select(n => $"word{i}x{n}"))))
            .ToList();
        var created = await client.PostAsJsonAsync("/api/records", SearchTestData.Request("Epic", "long record") with { Incidents = incidents });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var record = (await created.Content.ReadFromJsonAsync<RecordDto>())!;

        await SearchTestData.IndexAllAsync(factory);

        var chunks = await factory.ScalarAsync<long>($"SELECT count(*) FROM search_chunk WHERE record_id = '{record.Id}'");
        Assert.True(chunks >= 3);
        Assert.Equal(chunks, factory.Embeddings.BatchSizes.Sum());
    }

    [Theory]
    [InlineData("notes.txt", "Hyperdrive calibration manual for the cruiser fleet", "text/plain")]
    [InlineData("notes.md", "# Hyperdrive calibration manual\n\nfor the cruiser fleet", "text/markdown")]
    public async Task ATextArtifact_IsIndexedEndToEnd(string fileName, string text, string _)
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        var record = await ArtifactApi.CreateOwnRecordAsync(client);
        var artifact = await ArtifactApi.UploadOkAsync(client, record.Id, TestFiles.Text(text), fileName);

        await SearchTestData.IndexAllAsync(factory);

        Assert.Equal("indexed", await factory.ScalarAsync<string>($"SELECT index_status FROM artifact WHERE id = '{artifact.Id}'"));
        Assert.Null(await factory.ScalarAsync<string>($"SELECT index_detail FROM artifact WHERE id = '{artifact.Id}'"));
        Assert.Equal(1L, await factory.ScalarAsync<long>($"SELECT count(*) FROM search_chunk WHERE artifact_id = '{artifact.Id}' AND source_type = 'artifact'"));
        Assert.Contains("calibration", await factory.ScalarAsync<string>($"SELECT content FROM search_chunk WHERE artifact_id = '{artifact.Id}'"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task APdfAndADocx_AreIndexedLocally()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        var record = await ArtifactApi.CreateOwnRecordAsync(client);
        var pdf = await ArtifactApi.UploadOkAsync(client, record.Id, PdfBuilder.Text("Orders for the siege of Geonosis were signed by the council of masters."), "orders.pdf");
        var docx = await ArtifactApi.UploadOkAsync(client, record.Id, DocxBuilder.Create(["Quarterly report of the temple archives"]), "report.docx");

        await SearchTestData.IndexAllAsync(factory);

        foreach (var artifact in new[] { pdf, docx })
        {
            Assert.Equal("indexed", await factory.ScalarAsync<string>($"SELECT index_status FROM artifact WHERE id = '{artifact.Id}'"));
        }

        Assert.Empty(factory.Ocr.Calls);
    }

    [Fact]
    public async Task Images_AreNeverQueued_AndStayNotSearchable()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        var record = await ArtifactApi.CreateOwnRecordAsync(client);
        var image = await ArtifactApi.UploadOkAsync(client, record.Id, TestFiles.Png(), "map.png", "A star map");

        await SearchTestData.IndexAllAsync(factory);

        Assert.Equal("skipped", await factory.ScalarAsync<string>($"SELECT index_status FROM artifact WHERE id = '{image.Id}'"));
        Assert.Equal("unsupported_type", await factory.ScalarAsync<string>($"SELECT index_detail FROM artifact WHERE id = '{image.Id}'"));
        Assert.Equal(0L, await factory.ScalarAsync<long>($"SELECT count(*) FROM search_chunk WHERE artifact_id = '{image.Id}'"));
        Assert.Equal(0L, await factory.ScalarAsync<long>($"SELECT count(*) FROM search_index_job WHERE artifact_id = '{image.Id}'"));
    }

    [Fact]
    public async Task AScannedPdf_IsIndexedFromOcrText()
    {
        await using var factory = await CreateFactoryAsync();
        factory.Ocr.Text = "Distinctive phrase recovered from the scan";
        var client = factory.CreateUserClient();
        var record = await ArtifactApi.CreateOwnRecordAsync(client);
        var artifact = await ArtifactApi.UploadOkAsync(client, record.Id, PdfBuilder.Blank(2), "scan.pdf");

        await SearchTestData.IndexAllAsync(factory);

        Assert.Equal("indexed", await factory.ScalarAsync<string>($"SELECT index_status FROM artifact WHERE id = '{artifact.Id}'"));
        Assert.Contains("Distinctive phrase", await factory.ScalarAsync<string>($"SELECT content FROM search_chunk WHERE artifact_id = '{artifact.Id}'"), StringComparison.Ordinal);
        Assert.Single(factory.Ocr.Calls);
    }

    [Fact]
    public async Task AScannedPdf_WithOcrDisabled_IsSkippedWithTheReason()
    {
        await using var factory = await CreateFactoryAsync(o => o.OcrFallback.Enabled = false);
        var client = factory.CreateUserClient();
        var record = await ArtifactApi.CreateOwnRecordAsync(client);
        var artifact = await ArtifactApi.UploadOkAsync(client, record.Id, PdfBuilder.Blank(2), "scan.pdf");

        await SearchTestData.IndexAllAsync(factory);

        Assert.Equal("skipped", await factory.ScalarAsync<string>($"SELECT index_status FROM artifact WHERE id = '{artifact.Id}'"));
        Assert.Equal("ocr_disabled", await factory.ScalarAsync<string>($"SELECT index_detail FROM artifact WHERE id = '{artifact.Id}'"));
        Assert.Equal(0L, await factory.ScalarAsync<long>($"SELECT count(*) FROM search_chunk WHERE artifact_id = '{artifact.Id}'"));
        Assert.Equal("succeeded", await factory.ScalarAsync<string>($"SELECT status FROM search_index_job WHERE artifact_id = '{artifact.Id}'"));
    }

    [Fact]
    public async Task APartlyReadScan_RecordsThePageLimitNote()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        var record = await ArtifactApi.CreateOwnRecordAsync(client);
        var artifact = await ArtifactApi.UploadOkAsync(client, record.Id, PdfBuilder.Blank(120), "long-scan.pdf");

        await SearchTestData.IndexAllAsync(factory);

        Assert.Equal("indexed", await factory.ScalarAsync<string>($"SELECT index_status FROM artifact WHERE id = '{artifact.Id}'"));
        Assert.Equal("ocr_page_limit", await factory.ScalarAsync<string>($"SELECT index_detail FROM artifact WHERE id = '{artifact.Id}'"));
    }

    [Fact]
    public async Task ABlankTextFile_IsSkippedAsHavingNoText()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        var record = await ArtifactApi.CreateOwnRecordAsync(client);
        var artifact = await ArtifactApi.UploadOkAsync(client, record.Id, TestFiles.Text("   \n  "), "blank.txt");

        await SearchTestData.IndexAllAsync(factory);

        Assert.Equal("skipped", await factory.ScalarAsync<string>($"SELECT index_status FROM artifact WHERE id = '{artifact.Id}'"));
        Assert.Equal("no_text", await factory.ScalarAsync<string>($"SELECT index_detail FROM artifact WHERE id = '{artifact.Id}'"));
    }

    [Fact]
    public async Task AnUnreadableDocument_IsMarkedFailed_WithAParseError_WithoutRetries()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        var record = await ArtifactApi.CreateOwnRecordAsync(client);
        var artifact = await ArtifactApi.UploadOkAsync(client, record.Id, TestFiles.Docx(), "broken.docx");

        await SearchTestData.IndexAllAsync(factory);

        Assert.Equal("failed", await factory.ScalarAsync<string>($"SELECT index_status FROM artifact WHERE id = '{artifact.Id}'"));
        Assert.Equal("parse_error", await factory.ScalarAsync<string>($"SELECT index_detail FROM artifact WHERE id = '{artifact.Id}'"));
        Assert.Equal("succeeded", await factory.ScalarAsync<string>($"SELECT status FROM search_index_job WHERE artifact_id = '{artifact.Id}'"));
    }

    [Fact]
    public async Task AnArtifactRemovedBeforeIndexing_LeavesNothingBehind()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        var record = await ArtifactApi.CreateOwnRecordAsync(client);
        var artifact = await ArtifactApi.UploadOkAsync(client, record.Id, TestFiles.Text("soon removed words"), "gone.txt");
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/artifacts/{artifact.Id}")).StatusCode);

        await SearchTestData.IndexAllAsync(factory);

        Assert.Equal(0L, await factory.ScalarAsync<long>($"SELECT count(*) FROM search_chunk WHERE artifact_id = '{artifact.Id}'"));
        Assert.Equal("succeeded", await factory.ScalarAsync<string>($"SELECT status FROM search_index_job WHERE artifact_id = '{artifact.Id}'"));
    }

    [Fact]
    public async Task AnEmbeddingOutage_KeepsTheJobWaiting_WithoutUsingUpItsAttempts()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        var record = await ArtifactApi.CreateOwnRecordAsync(client);
        var artifact = await ArtifactApi.UploadOkAsync(client, record.Id, TestFiles.Text("indexable words here"), "notes.txt");
        await factory.ScalarAsync<int>("WITH d AS (DELETE FROM search_index_job WHERE kind = 'record' RETURNING 1) SELECT count(*)::int FROM d");
        factory.Embeddings.Failure = new HttpRequestException("embedding service down");
        var worker = Worker(factory);

        for (var round = 1; round <= IndexJobRepository.MaxAttempts + 3; round++)
        {
            await factory.ScalarAsync<int>("WITH u AS (UPDATE search_index_job SET available_at = now() - interval '1 second' RETURNING 1) SELECT count(*)::int FROM u");
            Assert.True(await worker.ProcessNextAsync(CancellationToken.None));
            Assert.False(await worker.ProcessNextAsync(CancellationToken.None));
        }

        Assert.Equal("pending", await factory.ScalarAsync<string>("SELECT status FROM search_index_job"));
        Assert.Equal(0, await factory.ScalarAsync<int>("SELECT attempts FROM search_index_job"));
        Assert.Equal("HttpRequestException", await factory.ScalarAsync<string>("SELECT last_error FROM search_index_job"));
        Assert.Equal("pending", await factory.ScalarAsync<string>($"SELECT index_status FROM artifact WHERE id = '{artifact.Id}'"));

        factory.Embeddings.Failure = null;
        await factory.ScalarAsync<int>("WITH u AS (UPDATE search_index_job SET available_at = now() - interval '1 second' RETURNING 1) SELECT count(*)::int FROM u");
        Assert.True(await worker.ProcessNextAsync(CancellationToken.None));
        Assert.Equal("indexed", await factory.ScalarAsync<string>($"SELECT index_status FROM artifact WHERE id = '{artifact.Id}'"));
    }

    [Fact]
    public async Task AnErrorThatIsNotAnOutage_RetriesWithBackoff_ThenFailsTheJobAndTheArtifact()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        var record = await ArtifactApi.CreateOwnRecordAsync(client);
        var artifact = await ArtifactApi.UploadOkAsync(client, record.Id, TestFiles.Text("indexable words here"), "notes.txt");
        await factory.ScalarAsync<int>("WITH d AS (DELETE FROM search_index_job WHERE kind = 'record' RETURNING 1) SELECT count(*)::int FROM d");
        factory.Embeddings.Failure = new InvalidOperationException("the service rejected this input");
        var worker = Worker(factory);

        Assert.True(await worker.ProcessNextAsync(CancellationToken.None));

        Assert.Equal("pending", await factory.ScalarAsync<string>("SELECT status FROM search_index_job"));
        Assert.Equal(1, await factory.ScalarAsync<int>("SELECT attempts FROM search_index_job"));
        Assert.Equal("InvalidOperationException", await factory.ScalarAsync<string>("SELECT last_error FROM search_index_job"));
        Assert.Equal("pending", await factory.ScalarAsync<string>($"SELECT index_status FROM artifact WHERE id = '{artifact.Id}'"));
        Assert.False(await worker.ProcessNextAsync(CancellationToken.None));

        for (var attempt = 2; attempt <= 5; attempt++)
        {
            await factory.ScalarAsync<int>("WITH u AS (UPDATE search_index_job SET available_at = now() - interval '1 second' RETURNING 1) SELECT count(*)::int FROM u");
            Assert.True(await worker.ProcessNextAsync(CancellationToken.None));
        }

        Assert.Equal("failed", await factory.ScalarAsync<string>("SELECT status FROM search_index_job"));
        Assert.Equal("failed", await factory.ScalarAsync<string>($"SELECT index_status FROM artifact WHERE id = '{artifact.Id}'"));
        Assert.Null(await factory.ScalarAsync<string>($"SELECT index_detail FROM artifact WHERE id = '{artifact.Id}'"));
    }

    [Fact]
    public async Task ARecoveredService_IndexesAfterEarlierFailures()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        var record = await SearchTestData.CreateRecordAsync(client, "Resilient", "survives outages");
        factory.Embeddings.Failure = new TimeoutException();
        var worker = Worker(factory);
        Assert.True(await worker.ProcessNextAsync(CancellationToken.None));
        Assert.Equal(0L, await factory.ScalarAsync<long>($"SELECT count(*) FROM search_chunk WHERE record_id = '{record.Id}'"));

        factory.Embeddings.Failure = null;
        await factory.ScalarAsync<int>("WITH u AS (UPDATE search_index_job SET available_at = now() - interval '1 second' RETURNING 1) SELECT count(*)::int FROM u");
        Assert.True(await worker.ProcessNextAsync(CancellationToken.None));

        Assert.Equal(1L, await factory.ScalarAsync<long>($"SELECT count(*) FROM search_chunk WHERE record_id = '{record.Id}'"));
    }

    [Fact]
    public async Task IndexingLogsOnlyIdentifiersAndCounts_NeverContentOrFileNames()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        var record = await SearchTestData.CreateRecordAsync(client, "Very Secret Title", "confidential narrative words");
        await ArtifactApi.UploadOkAsync(client, record.Id, TestFiles.Text("confidential file words"), "very-secret-name.txt");

        await SearchTestData.IndexAllAsync(factory);

        Assert.DoesNotContain(factory.Logs.Messages, m =>
            m.Contains("Very Secret Title", StringComparison.Ordinal)
            || m.Contains("confidential", StringComparison.Ordinal)
            || m.Contains("very-secret-name", StringComparison.Ordinal));
        Assert.Contains(factory.Logs.Messages, m => m.Contains(record.Id.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public void TheBackgroundLoop_IsOffWithoutEmbeddingsConfiguration()
    {
        Assert.False(SearchServiceCollectionExtensions.IndexingEnabled(new SearchOptions()));
        Assert.True(SearchServiceCollectionExtensions.IndexingEnabled(new SearchOptions { OpenAiEndpoint = "https://example.openai.azure.com" }));
        Assert.False(SearchServiceCollectionExtensions.IndexingEnabled(new SearchOptions { OpenAiEndpoint = "https://example.openai.azure.com", Indexing = { Enabled = false } }));
    }
}