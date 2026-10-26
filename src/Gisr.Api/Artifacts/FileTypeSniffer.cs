using System.IO.Compression;
using System.Text.Unicode;

namespace Gisr.Api.Artifacts;

public sealed record SniffedFile(string ContentType, bool IsImage);

// The stored content type comes from the file's own bytes; the browser-declared type is never trusted.
public static class FileTypeSniffer
{
    public const string Jpeg = "image/jpeg";
    public const string Png = "image/png";
    public const string WebP = "image/webp";
    public const string Gif = "image/gif";
    public const string Pdf = "application/pdf";
    public const string Docx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    public const string PlainText = "text/plain";
    public const string Markdown = "text/markdown";

    private const int PdfScanLength = 1024;

    private static readonly IReadOnlyDictionary<string, string[]> Extensions = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        [Jpeg] = [".jpg", ".jpeg"],
        [Png] = [".png"],
        [WebP] = [".webp"],
        [Gif] = [".gif"],
        [Pdf] = [".pdf"],
        [Docx] = [".docx"],
        [PlainText] = [".txt"],
        [Markdown] = [".md", ".markdown"],
    };

    public static SniffedFile? Sniff(ReadOnlySpan<byte> content, string fileName)
    {
        if (content.IsEmpty)
        {
            return null;
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var contentType = Detect(content, extension);
        if (contentType is null || !Extensions[contentType].Contains(extension, StringComparer.Ordinal))
        {
            return null;
        }

        return new SniffedFile(contentType, contentType.StartsWith("image/", StringComparison.Ordinal));
    }

    private static string? Detect(ReadOnlySpan<byte> content, string extension)
    {
        if (content.StartsWith<byte>([0xFF, 0xD8, 0xFF]))
        {
            return Jpeg;
        }

        if (content.StartsWith<byte>([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return Png;
        }

        if (content.StartsWith("GIF87a"u8) || content.StartsWith("GIF89a"u8))
        {
            return Gif;
        }

        if (content.Length >= 12 && content[..4].SequenceEqual("RIFF"u8) && content.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return WebP;
        }

        if (content[..Math.Min(content.Length, PdfScanLength)].IndexOf("%PDF-"u8) >= 0)
        {
            return Pdf;
        }

        if (content.StartsWith<byte>([0x50, 0x4B, 0x03, 0x04]))
        {
            return IsDocx(content) ? Docx : null;
        }

        if (extension is ".txt" or ".md" or ".markdown" && IsPlainText(content))
        {
            return extension == ".txt" ? PlainText : Markdown;
        }

        return null;
    }

    private static bool IsDocx(ReadOnlySpan<byte> content)
    {
        try
        {
            using var stream = new MemoryStream(content.ToArray(), writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            return archive.GetEntry("word/document.xml") is not null;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static bool IsPlainText(ReadOnlySpan<byte> content)
    {
        foreach (var b in content)
        {
            if (b < 0x20 && b is not (0x09 or 0x0A or 0x0C or 0x0D))
            {
                return false;
            }
        }

        return Utf8.IsValid(content);
    }
}
