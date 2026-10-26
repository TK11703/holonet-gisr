using Azure;
using Azure.AI.DocumentIntelligence;
using Gisr.Api.Artifacts;
using Gisr.Api.Search.Extraction;
using Gisr.Database.Search;
using Microsoft.Extensions.Options;

namespace Gisr.Api.Search.Ocr;

public interface IOcrClient
{
    bool IsAvailable { get; }

    // Reads at most the first `maxPages` pages and returns their plain text.
    Task<string> ReadTextAsync(byte[] pdf, int maxPages, CancellationToken cancellationToken);
}

public sealed class NoOcrClient : IOcrClient
{
    public bool IsAvailable => false;

    public Task<string> ReadTextAsync(byte[] pdf, int maxPages, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("OCR is not configured.");
}

public sealed class DocumentIntelligenceOcrClient(DocumentIntelligenceClient client) : IOcrClient
{
    public bool IsAvailable => true;

    public async Task<string> ReadTextAsync(byte[] pdf, int maxPages, CancellationToken cancellationToken)
    {
        var options = new AnalyzeDocumentOptions("prebuilt-read", BinaryData.FromBytes(pdf)) { Pages = $"1-{maxPages}" };
        var operation = await client.AnalyzeDocumentAsync(WaitUntil.Completed, options, cancellationToken);
        return operation.Value.Content ?? string.Empty;
    }
}

// Text PDFs are read locally; only a scanned PDF reaches the paid OCR service, and only within the monthly page budget.
public sealed class OcrFallbackExtractor(
    PdfExtractor pdf,
    IOcrClient ocr,
    OcrUsageRepository usage,
    IOptions<SearchOptions> options,
    TimeProvider clock) : ITextExtractor
{
    public const int MaxOcrPages = 100;

    public bool Supports(string contentType) => contentType == FileTypeSniffer.Pdf;

    public async Task<ExtractionResult> ExtractAsync(byte[] content, CancellationToken cancellationToken)
    {
        PdfExtraction extraction;
        try
        {
            extraction = await Task.Run(() => pdf.Inspect(content, cancellationToken), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ExtractionResult.Failed();
        }

        if (!extraction.LooksScanned)
        {
            return ExtractionResult.Extracted(extraction.Text);
        }

        var settings = options.Value.OcrFallback;
        if (!settings.Enabled || !ocr.IsAvailable)
        {
            return ExtractionResult.Skipped("ocr_disabled");
        }

        var pages = Math.Min(extraction.PageCount, MaxOcrPages);
        var now = clock.GetUtcNow();
        var month = new DateOnly(now.Year, now.Month, 1);
        if (!await usage.TryReservePagesAsync(month, pages, settings.MonthlyPageBudget, cancellationToken))
        {
            return ExtractionResult.Skipped("ocr_budget_exhausted");
        }

        var text = ExtractionLimits.Cap(await ocr.ReadTextAsync(content, pages, cancellationToken));
        if (string.IsNullOrWhiteSpace(text))
        {
            return ExtractionResult.Skipped("no_text");
        }

        return ExtractionResult.Extracted(text, extraction.PageCount > MaxOcrPages ? "ocr_page_limit" : null);
    }
}
