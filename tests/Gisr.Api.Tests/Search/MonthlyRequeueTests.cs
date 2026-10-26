using Gisr.Api.Search;
using Gisr.Api.Tests.Artifacts;
using Gisr.Api.Tests.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Gisr.Api.Tests.Search;

[Collection("api")]
public class MonthlyRequeueTests(PostgresFixture postgres, AzuriteFixture azurite)
{
    private static async Task<Guid> SkippedArtifactAsync(ApiFactory factory, HttpClient client, Guid recordId, string name, string detail)
    {
        var artifact = await ArtifactApi.UploadOkAsync(client, recordId, TestFiles.Pdf(name), name + ".pdf");
        await factory.ScalarAsync<int>($"WITH u AS (UPDATE artifact SET index_status = 'skipped', index_detail = '{detail}' WHERE id = '{artifact.Id}' RETURNING 1) SELECT count(*)::int FROM u");
        return artifact.Id;
    }

    [Fact]
    public async Task OnTheFirstRunOfANewMonth_BudgetSkippedFilesAreRequeued()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, azurite);
        var client = factory.CreateUserClient();
        var record = await ArtifactApi.CreateOwnRecordAsync(client);
        await SearchTestData.IndexAllAsync(factory);
        var budget = await SkippedArtifactAsync(factory, client, record.Id, "budget", "ocr_budget_exhausted");
        var disabled = await SkippedArtifactAsync(factory, client, record.Id, "disabled", "ocr_disabled");
        var noText = await SkippedArtifactAsync(factory, client, record.Id, "empty", "no_text");
        await factory.ScalarAsync<int>("WITH d AS (DELETE FROM search_index_job RETURNING 1) SELECT count(*)::int FROM d");
        var service = factory.Services.GetRequiredService<MonthlyRequeueService>();

        var requeued = await service.RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, requeued);
        Assert.Equal("pending", await factory.ScalarAsync<string>($"SELECT index_status FROM artifact WHERE id = '{budget}'"));
        Assert.Null(await factory.ScalarAsync<string>($"SELECT index_detail FROM artifact WHERE id = '{budget}'"));
        Assert.Equal(1L, await factory.ScalarAsync<long>($"SELECT count(*) FROM search_index_job WHERE artifact_id = '{budget}' AND status = 'pending'"));
        Assert.Equal("ocr_disabled", await factory.ScalarAsync<string>($"SELECT index_detail FROM artifact WHERE id = '{disabled}'"));
        Assert.Equal("no_text", await factory.ScalarAsync<string>($"SELECT index_detail FROM artifact WHERE id = '{noText}'"));
    }

    [Fact]
    public async Task LaterRunsInTheSameMonth_DoNothing_UntilTheMonthChanges()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, azurite);
        var client = factory.CreateUserClient();
        var record = await ArtifactApi.CreateOwnRecordAsync(client);
        var artifact = await SkippedArtifactAsync(factory, client, record.Id, "budget", "ocr_budget_exhausted");
        var service = factory.Services.GetRequiredService<MonthlyRequeueService>();
        Assert.Equal(1, await service.RunOnceAsync(CancellationToken.None));

        await factory.ScalarAsync<int>($"WITH u AS (UPDATE artifact SET index_status = 'skipped', index_detail = 'ocr_budget_exhausted' WHERE id = '{artifact}' RETURNING 1) SELECT count(*)::int FROM u");
        factory.Clock.Now = factory.Clock.Now.AddDays(10);
        Assert.Equal(0, await service.RunOnceAsync(CancellationToken.None));
        Assert.Equal("skipped", await factory.ScalarAsync<string>($"SELECT index_status FROM artifact WHERE id = '{artifact}'"));

        factory.Clock.Now = new DateTimeOffset(2026, 11, 1, 0, 5, 0, TimeSpan.Zero);
        Assert.Equal(1, await service.RunOnceAsync(CancellationToken.None));
        Assert.Equal("pending", await factory.ScalarAsync<string>($"SELECT index_status FROM artifact WHERE id = '{artifact}'"));
    }

    [Fact]
    public async Task ARequeuedFile_IsReindexedByTheWorker()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, azurite);
        var client = factory.CreateUserClient();
        var record = await ArtifactApi.CreateOwnRecordAsync(client);
        var artifact = await ArtifactApi.UploadOkAsync(client, record.Id, PdfBuilder.Blank(1), "scan.pdf");
        factory.Ocr.IsAvailable = false;
        await SearchTestData.IndexAllAsync(factory);
        Assert.Equal("ocr_disabled", await factory.ScalarAsync<string>($"SELECT index_detail FROM artifact WHERE id = '{artifact.Id}'"));
        await factory.ScalarAsync<int>($"WITH u AS (UPDATE artifact SET index_detail = 'ocr_budget_exhausted' WHERE id = '{artifact.Id}' RETURNING 1) SELECT count(*)::int FROM u");
        factory.Ocr.IsAvailable = true;

        await factory.Services.GetRequiredService<MonthlyRequeueService>().RunOnceAsync(CancellationToken.None);
        await SearchTestData.IndexAllAsync(factory);

        Assert.Equal("indexed", await factory.ScalarAsync<string>($"SELECT index_status FROM artifact WHERE id = '{artifact.Id}'"));
    }
}
