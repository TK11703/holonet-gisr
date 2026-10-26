using Gisr.Api.Artifacts;
using Gisr.Api.Search;
using Gisr.Api.Search.Extraction;
using Gisr.Api.Search.Ocr;
using Gisr.Api.Tests.Common;
using Gisr.Database.Search;
using Microsoft.Extensions.DependencyInjection;

namespace Gisr.Api.Tests.Search;

[Collection("api")]
public class OcrFallbackTests(PostgresFixture postgres)
{
    private static readonly DateOnly CurrentMonth = new(2026, 10, 1);

    private async Task<ApiFactory> CreateFactoryAsync(Action<SearchOptions>? configure = null)
    {
        var factory = await ApiFactory.CreateAsync(postgres);
        factory.SearchOptionsOverride = configure;
        return factory;
    }

    private static OcrFallbackExtractor Extractor(ApiFactory factory) => factory.Services.GetRequiredService<OcrFallbackExtractor>();

    private static OcrUsageRepository Usage(ApiFactory factory) => factory.Services.GetRequiredService<OcrUsageRepository>();

    [Fact]
    public async Task ATextPdf_IsReadLocally_AndNeverCallsOcr()
    {
        await using var factory = await CreateFactoryAsync();
        var pdf = PdfBuilder.Text("The Jedi temple on Coruscant is guarded by many knights every day of the week.");

        var result = await Extractor(factory).ExtractAsync(pdf, CancellationToken.None);

        Assert.Equal(ExtractionStatus.Extracted, result.Status);
        Assert.Contains("Jedi temple", result.Text, StringComparison.Ordinal);
        Assert.Null(result.Detail);
        Assert.Empty(factory.Ocr.Calls);
        Assert.Equal(0, await Usage(factory).GetPagesUsedAsync(CurrentMonth, CancellationToken.None));
    }

    [Fact]
    public async Task DocxAndTextFiles_NeverCallOcr()
    {
        await using var factory = await CreateFactoryAsync();
        var selector = factory.Services.GetRequiredService<DocumentTextExtractor>();

        var docx = await selector.ExtractAsync(FileTypeSniffer.Docx, DocxBuilder.Create(["Some body text here"]), CancellationToken.None);
        var text = await selector.ExtractAsync("text/plain", "plain words"u8.ToArray(), CancellationToken.None);

        Assert.Equal(ExtractionStatus.Extracted, docx.Status);
        Assert.Equal(ExtractionStatus.Extracted, text.Status);
        Assert.Empty(factory.Ocr.Calls);
    }

    [Fact]
    public async Task AScannedPdf_IsSentToOcr_AndItsPagesAreReserved()
    {
        await using var factory = await CreateFactoryAsync();
        factory.Ocr.Text = "Distinctive phrase from the scan";

        var result = await Extractor(factory).ExtractAsync(PdfBuilder.Blank(3), CancellationToken.None);

        Assert.Equal(ExtractionStatus.Extracted, result.Status);
        Assert.Equal("Distinctive phrase from the scan", result.Text);
        Assert.Null(result.Detail);
        var call = Assert.Single(factory.Ocr.Calls);
        Assert.Equal(3, call.MaxPages);
        Assert.Equal(3, await Usage(factory).GetPagesUsedAsync(CurrentMonth, CancellationToken.None));
    }

    [Fact]
    public async Task OnlyTheFirst100PagesAreRead_AndTheTruncationIsNoted()
    {
        await using var factory = await CreateFactoryAsync();

        var result = await Extractor(factory).ExtractAsync(PdfBuilder.Blank(130), CancellationToken.None);

        Assert.Equal(ExtractionStatus.Extracted, result.Status);
        Assert.Equal("ocr_page_limit", result.Detail);
        Assert.Equal(100, Assert.Single(factory.Ocr.Calls).MaxPages);
        Assert.Equal(100, await Usage(factory).GetPagesUsedAsync(CurrentMonth, CancellationToken.None));
    }

    [Fact]
    public async Task WhenOcrIsDisabled_TheScanIsSkipped_WithoutACallOrAReservation()
    {
        await using var factory = await CreateFactoryAsync(o => o.OcrFallback.Enabled = false);

        var result = await Extractor(factory).ExtractAsync(PdfBuilder.Blank(2), CancellationToken.None);

        Assert.Equal(ExtractionStatus.Skipped, result.Status);
        Assert.Equal("ocr_disabled", result.Detail);
        Assert.Empty(factory.Ocr.Calls);
        Assert.Equal(0, await Usage(factory).GetPagesUsedAsync(CurrentMonth, CancellationToken.None));
    }

    [Fact]
    public async Task WhenNoOcrServiceIsConfigured_TheScanIsSkippedAsDisabled()
    {
        await using var factory = await CreateFactoryAsync();
        factory.Ocr.IsAvailable = false;

        var result = await Extractor(factory).ExtractAsync(PdfBuilder.Blank(2), CancellationToken.None);

        Assert.Equal("ocr_disabled", result.Detail);
        Assert.Empty(factory.Ocr.Calls);
    }

    [Fact]
    public async Task WhenTheMonthlyBudgetIsUsedUp_TheScanIsSkipped_WithoutACall()
    {
        await using var factory = await CreateFactoryAsync(o => o.OcrFallback.MonthlyPageBudget = 5);
        var extractor = Extractor(factory);
        Assert.Equal(ExtractionStatus.Extracted, (await extractor.ExtractAsync(PdfBuilder.Blank(3), CancellationToken.None)).Status);

        var result = await extractor.ExtractAsync(PdfBuilder.Blank(3), CancellationToken.None);

        Assert.Equal(ExtractionStatus.Skipped, result.Status);
        Assert.Equal("ocr_budget_exhausted", result.Detail);
        Assert.Single(factory.Ocr.Calls);
        Assert.Equal(3, await Usage(factory).GetPagesUsedAsync(CurrentMonth, CancellationToken.None));
    }

    [Fact]
    public async Task TheBudgetIsPerMonth()
    {
        await using var factory = await CreateFactoryAsync(o => o.OcrFallback.MonthlyPageBudget = 3);
        var extractor = Extractor(factory);
        Assert.Equal(ExtractionStatus.Extracted, (await extractor.ExtractAsync(PdfBuilder.Blank(3), CancellationToken.None)).Status);
        Assert.Equal("ocr_budget_exhausted", (await extractor.ExtractAsync(PdfBuilder.Blank(1), CancellationToken.None)).Detail);

        factory.Clock.Now = new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);

        Assert.Equal(ExtractionStatus.Extracted, (await extractor.ExtractAsync(PdfBuilder.Blank(1), CancellationToken.None)).Status);
    }

    [Fact]
    public async Task AScanWithoutRecognisedText_IsSkippedAsHavingNoText()
    {
        await using var factory = await CreateFactoryAsync();
        factory.Ocr.Text = "   ";

        var result = await Extractor(factory).ExtractAsync(PdfBuilder.Blank(1), CancellationToken.None);

        Assert.Equal(ExtractionStatus.Skipped, result.Status);
        Assert.Equal("no_text", result.Detail);
    }

    [Fact]
    public async Task AnOcrFailure_PropagatesForARetry_AndTheReservationIsNotRefunded()
    {
        await using var factory = await CreateFactoryAsync();
        factory.Ocr.Failure = new HttpRequestException("service down");

        await Assert.ThrowsAsync<HttpRequestException>(() => Extractor(factory).ExtractAsync(PdfBuilder.Blank(2), CancellationToken.None));

        Assert.Equal(2, await Usage(factory).GetPagesUsedAsync(CurrentMonth, CancellationToken.None));
    }

    [Fact]
    public async Task ACorruptPdf_Fails_WithAParseError_WithoutOcr()
    {
        await using var factory = await CreateFactoryAsync();

        var result = await Extractor(factory).ExtractAsync("%PDF-1.4 not really a pdf"u8.ToArray(), CancellationToken.None);

        Assert.Equal(ExtractionStatus.Failed, result.Status);
        Assert.Equal("parse_error", result.Detail);
        Assert.Empty(factory.Ocr.Calls);
    }

    [Fact]
    public async Task ImagesAndUnknownTypes_AreSkippedAsUnsupported()
    {
        await using var factory = await CreateFactoryAsync();
        var selector = factory.Services.GetRequiredService<DocumentTextExtractor>();

        var result = await selector.ExtractAsync("image/png", [1, 2, 3], CancellationToken.None);

        Assert.Equal(ExtractionStatus.Skipped, result.Status);
        Assert.Equal("unsupported_type", result.Detail);
        Assert.Empty(factory.Ocr.Calls);
    }

    [Fact]
    public void TheDefaults_AreOcrOnWithAMonthlyBudgetOf2000Pages()
    {
        var options = new SearchOptions();

        Assert.True(options.OcrFallback.Enabled);
        Assert.Equal(2000, options.OcrFallback.MonthlyPageBudget);
        Assert.Equal("text-embedding-3-large", options.EmbeddingDeployment);
    }
}