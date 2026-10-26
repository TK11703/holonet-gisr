using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.AI;

namespace Gisr.Api.Tests.Search;

// Bag-of-words hashing: texts sharing words are close in cosine distance, unrelated ones are orthogonal.
public sealed class FakeEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    public const int Dimensions = 1536;

    private readonly List<int> _batchSizes = [];

    public Exception? Failure { get; set; }

    public IReadOnlyList<int> BatchSizes
    {
        get
        {
            lock (_batchSizes)
            {
                return [.. _batchSizes];
            }
        }
    }

    public static float[] Embed(string text)
    {
        var vector = new float[Dimensions];
        foreach (var word in text.ToLowerInvariant().Split([' ', '\n', '\r', '\t', '.', ',', ':', ';', '-', '(', ')', '"', '\'', '!', '?'], StringSplitOptions.RemoveEmptyEntries))
        {
            vector[Hash(word) % Dimensions] += 1;
        }

        var norm = MathF.Sqrt(vector.Sum(v => v * v));
        if (norm == 0)
        {
            vector[0] = 1;
            return vector;
        }

        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] /= norm;
        }

        return vector;
    }

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (Failure is { } failure)
        {
            throw failure;
        }

        var inputs = values.ToList();
        lock (_batchSizes)
        {
            _batchSizes.Add(inputs.Count);
        }

        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>([.. inputs.Select(text => new Embedding<float>(Embed(text)))]));
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }

    private static int Hash(string word)
    {
        var hash = 2166136261u;
        foreach (var c in word)
        {
            hash = (hash ^ c) * 16777619u;
        }

        return (int)(hash & 0x7FFFFFFF);
    }
}

public sealed class FakeOcrClient : Gisr.Api.Search.Ocr.IOcrClient
{
    private readonly List<(int Bytes, int MaxPages)> _calls = [];

    public bool IsAvailable { get; set; } = true;

    public string Text { get; set; } = "Recognised scanned text";

    public Exception? Failure { get; set; }

    public IReadOnlyList<(int Bytes, int MaxPages)> Calls
    {
        get
        {
            lock (_calls)
            {
                return [.. _calls];
            }
        }
    }

    public Task<string> ReadTextAsync(byte[] pdf, int maxPages, CancellationToken cancellationToken)
    {
        lock (_calls)
        {
            _calls.Add((pdf.Length, maxPages));
        }

        return Failure is { } failure ? throw failure : Task.FromResult(Text);
    }
}

public sealed class TestClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

internal static class PdfBuilder
{
    public static byte[] Text(params string[] pages) => Build(pages);

    // Pages without any text, like a scan whose content is only images.
    public static byte[] Blank(int pages) => Build([.. Enumerable.Repeat(string.Empty, pages)]);

    private static byte[] Build(string[] pages)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [" + string.Join(" ", pages.Select((_, i) => $"{4 + (i * 2)} 0 R")) + $"] /Count {pages.Length} >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        };

        for (var i = 0; i < pages.Length; i++)
        {
            var stream = pages[i].Length == 0 ? string.Empty : $"BT /F1 12 Tf 72 700 Td ({Escape(pages[i])}) Tj ET";
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents {5 + (i * 2)} 0 R /Resources << /Font << /F1 3 0 R >> >> >>");
            objects.Add($"<< /Length {stream.Length} >>\nstream\n{stream}\nendstream");
        }

        var builder = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(builder.Length);
            builder.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xref = builder.Length;
        builder.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            builder.Append($"{offset:D10} 00000 n \n");
        }

        builder.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(builder.ToString());
    }

    private static string Escape(string text) => text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("(", "\\(", StringComparison.Ordinal).Replace(")", "\\)", StringComparison.Ordinal);
}

internal static class DocxBuilder
{
    public static byte[] Create(string[] paragraphs, string[][]? tableRows = null, string? headerText = null, bool externalLink = false)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            var body = new Body();
            foreach (var text in paragraphs)
            {
                body.Append(new Paragraph(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve })));
            }

            if (tableRows is not null)
            {
                var table = new Table();
                foreach (var row in tableRows)
                {
                    table.Append(new TableRow(row.Select(cell => (OpenXmlElement)new TableCell(new Paragraph(new Run(new Text(cell))))).ToArray()));
                }

                body.Append(table);
            }

            main.Document = new Document(body);
            if (headerText is not null)
            {
                var header = main.AddNewPart<HeaderPart>();
                header.Header = new Header(new Paragraph(new Run(new Text(headerText))));
            }

            if (externalLink)
            {
                main.AddHyperlinkRelationship(new Uri("http://127.0.0.1:9/never-fetched"), isExternal: true);
                main.AddExternalRelationship("http://schemas.openxmlformats.org/officeDocument/2006/relationships/oleObject", new Uri("http://127.0.0.1:9/ole"));
            }

            main.Document.Save();
        }

        return stream.ToArray();
    }
}
