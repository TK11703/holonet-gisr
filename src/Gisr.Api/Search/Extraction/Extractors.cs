using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Gisr.Api.Artifacts;
using UglyToad.PdfPig;

namespace Gisr.Api.Search.Extraction;

public enum ExtractionStatus
{
    Extracted,
    Skipped,
    Failed,
}

// Detail is a reason code (`no_text`, `ocr_disabled`, ...) or a note on a partial read (`ocr_page_limit`); never content.
public sealed record ExtractionResult(ExtractionStatus Status, string Text = "", string? Detail = null)
{
    public static ExtractionResult Extracted(string text, string? detail = null) => new(ExtractionStatus.Extracted, text, detail);

    public static ExtractionResult Skipped(string detail) => new(ExtractionStatus.Skipped, string.Empty, detail);

    public static ExtractionResult Failed(string detail = "parse_error") => new(ExtractionStatus.Failed, string.Empty, detail);
}

public static class ExtractionLimits
{
    public const int MaxTextCharacters = 200_000;

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    public static string Cap(string text) => text.Length <= MaxTextCharacters ? text : text[..MaxTextCharacters];
}

public interface ITextExtractor
{
    bool Supports(string contentType);

    Task<ExtractionResult> ExtractAsync(byte[] content, CancellationToken cancellationToken);
}

// Untrusted files are parsed only here, in the background worker, each under a timeout.
public sealed class DocumentTextExtractor(IEnumerable<ITextExtractor> extractors)
{
    private readonly ITextExtractor[] _extractors = [.. extractors];

    public async Task<ExtractionResult> ExtractAsync(string contentType, byte[] content, CancellationToken cancellationToken)
    {
        var extractor = _extractors.FirstOrDefault(e => e.Supports(contentType));
        if (extractor is null)
        {
            return ExtractionResult.Skipped("unsupported_type");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ExtractionLimits.Timeout);
        try
        {
            return await extractor.ExtractAsync(content, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ExtractionResult.Failed();
        }
    }
}

public sealed class PlainTextExtractor : ITextExtractor
{
    public bool Supports(string contentType) => contentType is FileTypeSniffer.PlainText or FileTypeSniffer.Markdown;

    public Task<ExtractionResult> ExtractAsync(byte[] content, CancellationToken cancellationToken)
    {
        var text = Encoding.UTF8.GetString(content).TrimStart('\uFEFF');
        return Task.FromResult(string.IsNullOrWhiteSpace(text)
            ? ExtractionResult.Skipped("no_text")
            : ExtractionResult.Extracted(ExtractionLimits.Cap(text)));
    }
}

public sealed class DocxExtractor(long maxDecompressedBytes = DocxExtractor.DefaultMaxDecompressedBytes) : ITextExtractor
{
    public const long DefaultMaxDecompressedBytes = 100L * 1024 * 1024;

    public bool Supports(string contentType) => contentType == FileTypeSniffer.Docx;

    public Task<ExtractionResult> ExtractAsync(byte[] content, CancellationToken cancellationToken)
    {
        try
        {
            using (var archive = new ZipArchive(new MemoryStream(content, writable: false), ZipArchiveMode.Read))
            {
                if (archive.Entries.Sum(entry => entry.Length) > maxDecompressedBytes)
                {
                    return Task.FromResult(ExtractionResult.Skipped("too_large"));
                }
            }

            // Only the document body is read; relationships (including external ones) are never followed.
            using var document = WordprocessingDocument.Open(new MemoryStream(content, writable: false), isEditable: false);
            var body = document.MainDocumentPart?.Document?.Body;
            var text = new StringBuilder();
            foreach (var paragraph in body?.Descendants<Paragraph>() ?? [])
            {
                var line = string.Concat(paragraph.Descendants<Text>().Select(t => t.Text));
                if (!string.IsNullOrWhiteSpace(line))
                {
                    text.AppendLine(line);
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (text.Length >= ExtractionLimits.MaxTextCharacters)
                {
                    break;
                }
            }

            return Task.FromResult(text.Length == 0 || string.IsNullOrWhiteSpace(text.ToString())
                ? ExtractionResult.Skipped("no_text")
                : ExtractionResult.Extracted(ExtractionLimits.Cap(text.ToString().TrimEnd())));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Task.FromResult(ExtractionResult.Failed());
        }
    }
}

public sealed record PdfExtraction(string Text, int PageCount, bool LooksScanned);

public sealed class PdfExtractor(int maxPages = PdfExtractor.DefaultMaxPages)
{
    public const int DefaultMaxPages = 500;
    public const int MinCharactersPerPage = 25;

    // Throws for unreadable files; the caller reports a parse error.
    public PdfExtraction Inspect(byte[] content, CancellationToken cancellationToken)
    {
        using var document = PdfDocument.Open(content);
        var pageCount = document.NumberOfPages;
        var examined = Math.Min(pageCount, maxPages);
        var text = new StringBuilder();
        long visibleCharacters = 0;

        for (var number = 1; number <= examined; number++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = string.Join(' ', document.GetPage(number).GetWords().Select(word => word.Text));
            visibleCharacters += page.Count(c => !char.IsWhiteSpace(c));
            if (text.Length < ExtractionLimits.MaxTextCharacters)
            {
                text.AppendLine(page);
            }
        }

        var scanned = examined == 0 || (double)visibleCharacters / examined < MinCharactersPerPage;
        return new PdfExtraction(ExtractionLimits.Cap(text.ToString().TrimEnd()), pageCount, scanned);
    }
}
