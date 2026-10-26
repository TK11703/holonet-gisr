using System.Text;
using Gisr.Api.Search.Extraction;
using UglyToad.PdfPig;

namespace Gisr.Api.Tests.Search;

public class PlainTextExtractorTests
{
    private readonly PlainTextExtractor _extractor = new();

    [Theory]
    [InlineData("text/plain", true)]
    [InlineData("text/markdown", true)]
    [InlineData("application/pdf", false)]
    [InlineData("image/png", false)]
    public void SupportsTextAndMarkdownOnly(string contentType, bool expected) =>
        Assert.Equal(expected, _extractor.Supports(contentType));

    [Fact]
    public async Task ReadsUtf8Text()
    {
        var result = await _extractor.ExtractAsync(Encoding.UTF8.GetBytes("Caf\u00e9 patrol at dawn\nSecond line"), CancellationToken.None);

        Assert.Equal(ExtractionStatus.Extracted, result.Status);
        Assert.Equal("Caf\u00e9 patrol at dawn\nSecond line", result.Text);
    }

    [Fact]
    public async Task StripsAByteOrderMark()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("Hello")).ToArray();

        var result = await _extractor.ExtractAsync(bytes, CancellationToken.None);

        Assert.Equal("Hello", result.Text);
    }

    [Fact]
    public async Task MarkdownIsKeptAsWritten()
    {
        var result = await _extractor.ExtractAsync(Encoding.UTF8.GetBytes("# Heading\n\n- item"), CancellationToken.None);

        Assert.Equal("# Heading\n\n- item", result.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\t ")]
    public async Task EmptyOrBlankFiles_AreSkippedAsHavingNoText(string text)
    {
        var result = await _extractor.ExtractAsync(Encoding.UTF8.GetBytes(text), CancellationToken.None);

        Assert.Equal(ExtractionStatus.Skipped, result.Status);
        Assert.Equal("no_text", result.Detail);
    }

    [Fact]
    public async Task TheTextIsCappedAt200000Characters()
    {
        var result = await _extractor.ExtractAsync(Encoding.UTF8.GetBytes(new string('a', 250_000)), CancellationToken.None);

        Assert.Equal(ExtractionStatus.Extracted, result.Status);
        Assert.Equal(ExtractionLimits.MaxTextCharacters, result.Text.Length);
    }
}

public class DocxExtractorTests
{
    private static readonly string[] Paragraphs = ["Body paragraph about the Jedi temple.", "A second paragraph."];

    [Fact]
    public void SupportsOnlyDocx()
    {
        var extractor = new DocxExtractor();

        Assert.True(extractor.Supports("application/vnd.openxmlformats-officedocument.wordprocessingml.document"));
        Assert.False(extractor.Supports("application/pdf"));
    }

    [Fact]
    public async Task ReadsBodyParagraphs_AndTableCells()
    {
        var bytes = DocxBuilder.Create(Paragraphs, [["Name", "Rank"], ["Rex", "Captain"]]);

        var result = await new DocxExtractor().ExtractAsync(bytes, CancellationToken.None);

        Assert.Equal(ExtractionStatus.Extracted, result.Status);
        foreach (var expected in new[] { "Body paragraph about the Jedi temple.", "A second paragraph.", "Name", "Rank", "Rex", "Captain" })
        {
            Assert.Contains(expected, result.Text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task OnlyTheBodyIsRead_NotHeaders()
    {
        var bytes = DocxBuilder.Create(Paragraphs, headerText: "Secret header text");

        var result = await new DocxExtractor().ExtractAsync(bytes, CancellationToken.None);

        Assert.DoesNotContain("Secret header text", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExternalRelationships_AreNeverFollowedOrReported()
    {
        var bytes = DocxBuilder.Create(Paragraphs, externalLink: true);

        var result = await new DocxExtractor().ExtractAsync(bytes, CancellationToken.None);

        Assert.Equal(ExtractionStatus.Extracted, result.Status);
        Assert.DoesNotContain("127.0.0.1", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADocumentBeyondTheDecompressedSizeCap_IsSkippedAsTooLarge()
    {
        var bytes = DocxBuilder.Create(Paragraphs);

        var result = await new DocxExtractor(maxDecompressedBytes: 500).ExtractAsync(bytes, CancellationToken.None);

        Assert.Equal(ExtractionStatus.Skipped, result.Status);
        Assert.Equal("too_large", result.Detail);
    }

    [Fact]
    public void TheDefaultCapIs100MB() =>
        Assert.Equal(100L * 1024 * 1024, DocxExtractor.DefaultMaxDecompressedBytes);

    [Fact]
    public async Task ACorruptDocument_Fails_WithAParseError()
    {
        var result = await new DocxExtractor().ExtractAsync(Encoding.UTF8.GetBytes("this is not a zip"), CancellationToken.None);

        Assert.Equal(ExtractionStatus.Failed, result.Status);
        Assert.Equal("parse_error", result.Detail);
    }

    [Fact]
    public async Task AnEmptyDocument_IsSkippedAsHavingNoText()
    {
        var result = await new DocxExtractor().ExtractAsync(DocxBuilder.Create([]), CancellationToken.None);

        Assert.Equal(ExtractionStatus.Skipped, result.Status);
        Assert.Equal("no_text", result.Detail);
    }

    [Fact]
    public async Task TheTextIsCappedAt200000Characters()
    {
        var bytes = DocxBuilder.Create([new string('a', 150_000), new string('b', 150_000)]);

        var result = await new DocxExtractor().ExtractAsync(bytes, CancellationToken.None);

        Assert.Equal(ExtractionLimits.MaxTextCharacters, result.Text.Length);
    }
}

public class PdfExtractorTests
{
    [Fact]
    public void ThePdfBuilderProducesFilesPdfPigCanRead()
    {
        using var document = PdfDocument.Open(PdfBuilder.Text("Hello holonet"));

        Assert.Equal(1, document.NumberOfPages);
    }

    [Fact]
    public void ReadsTheTextOfATextPdf_PageByPage()
    {
        var extraction = new PdfExtractor().Inspect(
            PdfBuilder.Text("The Jedi temple on Coruscant is guarded by many knights every day of the week.", "A second page with plenty of readable words for the average."),
            CancellationToken.None);

        Assert.False(extraction.LooksScanned);
        Assert.Equal(2, extraction.PageCount);
        Assert.Contains("Jedi temple on Coruscant", extraction.Text, StringComparison.Ordinal);
        Assert.Contains("second page", extraction.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void APdfWithoutText_LooksScanned()
    {
        var extraction = new PdfExtractor().Inspect(PdfBuilder.Blank(3), CancellationToken.None);

        Assert.True(extraction.LooksScanned);
        Assert.Equal(3, extraction.PageCount);
        Assert.True(string.IsNullOrWhiteSpace(extraction.Text));
    }

    [Fact]
    public void FewerThan25CharactersPerPageOnAverage_LooksScanned()
    {
        var sparse = new PdfExtractor().Inspect(PdfBuilder.Text("Page 1", "Page 2", "Page 3"), CancellationToken.None);
        var enough = new PdfExtractor().Inspect(PdfBuilder.Text(new string('a', 25) + " b", new string('c', 30)), CancellationToken.None);

        Assert.True(sparse.LooksScanned);
        Assert.False(enough.LooksScanned);
    }

    [Fact]
    public void TheAverageDecidesNotEveryPage()
    {
        var extraction = new PdfExtractor().Inspect(
            PdfBuilder.Text(new string('x', 200), string.Empty, string.Empty), CancellationToken.None);

        Assert.False(extraction.LooksScanned);
    }

    [Fact]
    public void OnlyTheFirstPagesUpToTheCapAreRead()
    {
        var extraction = new PdfExtractor(maxPages: 2).Inspect(
            PdfBuilder.Text("first page text content", "second page text content", "third page text content"), CancellationToken.None);

        Assert.Equal(3, extraction.PageCount);
        Assert.Contains("second", extraction.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("third", extraction.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDefaultPageCapIs500() => Assert.Equal(500, PdfExtractor.DefaultMaxPages);

    [Fact]
    public void TheTextIsCappedAt200000Characters()
    {
        var pages = Enumerable.Range(0, 3).Select(i => string.Join(' ', Enumerable.Repeat($"word{i}", 12_000))).ToArray();

        var extraction = new PdfExtractor().Inspect(PdfBuilder.Text(pages), CancellationToken.None);

        Assert.Equal(ExtractionLimits.MaxTextCharacters, extraction.Text.Length);
    }

    [Fact]
    public void ACancelledToken_StopsTheExtraction()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => new PdfExtractor().Inspect(PdfBuilder.Text("abc"), cts.Token));
    }

    [Fact]
    public void ACorruptPdf_Throws_ForTheCallerToReportAParseError()
    {
        Assert.ThrowsAny<Exception>(() => new PdfExtractor().Inspect(Encoding.ASCII.GetBytes("%PDF-1.4 garbage with no structure"), CancellationToken.None));
    }

    [Fact]
    public void TheTimeoutIs60Seconds() => Assert.Equal(TimeSpan.FromSeconds(60), ExtractionLimits.Timeout);
}
