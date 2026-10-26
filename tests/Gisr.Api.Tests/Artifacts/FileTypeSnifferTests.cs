using System.Text;
using Gisr.Api.Artifacts;

namespace Gisr.Api.Tests.Artifacts;

public class FileTypeSnifferTests
{
    public static TheoryData<string, byte[], string, bool> Allowed => new()
    {
        { "photo.jpg", TestFiles.Jpeg(), "image/jpeg", true },
        { "photo.JPEG", TestFiles.Jpeg(), "image/jpeg", true },
        { "map.png", TestFiles.Png(), "image/png", true },
        { "pic.webp", TestFiles.WebP(), "image/webp", true },
        { "anim.gif", TestFiles.Gif(), "image/gif", true },
        { "orders.pdf", TestFiles.Pdf(), "application/pdf", false },
        { "report.docx", TestFiles.Docx(), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", false },
        { "notes.txt", TestFiles.Text(), "text/plain", false },
        { "notes.md", TestFiles.Text("# Heading\n\nBody"), "text/markdown", false },
        { "notes.markdown", TestFiles.Text("# Heading"), "text/markdown", false },
    };

    [Theory]
    [MemberData(nameof(Allowed))]
    public void RecognisesAllowedTypes_FromTheirBytes(string fileName, byte[] bytes, string contentType, bool isImage)
    {
        var result = FileTypeSniffer.Sniff(bytes, fileName);

        Assert.NotNull(result);
        Assert.Equal(contentType, result.ContentType);
        Assert.Equal(isImage, result.IsImage);
    }

    [Theory]
    [InlineData("evil.pdf")]
    [InlineData("evil.png")]
    [InlineData("evil.docx")]
    [InlineData("evil.txt")]
    [InlineData("evil.exe")]
    public void RejectsARenamedExecutable(string fileName) =>
        Assert.Null(FileTypeSniffer.Sniff(TestFiles.Exe(), fileName));

    [Fact]
    public void RejectsAZipThatIsNotADocx() =>
        Assert.Null(FileTypeSniffer.Sniff(TestFiles.Zip("readme.txt", "payload.bin"), "report.docx"));

    [Fact]
    public void RejectsACorruptZipNamedDocx() =>
        Assert.Null(FileTypeSniffer.Sniff([0x50, 0x4B, 0x03, 0x04, 0x00, 0x01, 0x02], "report.docx"));

    [Fact]
    public void RejectsEmptyFiles()
    {
        Assert.Null(FileTypeSniffer.Sniff([], "empty.txt"));
        Assert.Null(FileTypeSniffer.Sniff([], "empty.pdf"));
    }

    [Theory]
    [InlineData("scan.pdf")]
    [InlineData("scan.txt")]
    [InlineData("scan.jpg")]
    [InlineData("scan")]
    public void RejectsAnImageWhoseExtensionDoesNotMatch(string fileName) =>
        Assert.Null(FileTypeSniffer.Sniff(TestFiles.Png(), fileName));

    [Fact]
    public void RejectsAPdfWhoseExtensionDoesNotMatch()
    {
        Assert.Null(FileTypeSniffer.Sniff(TestFiles.Pdf(), "orders.png"));
        Assert.Null(FileTypeSniffer.Sniff(TestFiles.Pdf(), "orders.docx"));
    }

    [Theory]
    [InlineData("notes.csv")]
    [InlineData("script.js")]
    [InlineData("noextension")]
    public void RejectsTextWithAnUnlistedExtension(string fileName) =>
        Assert.Null(FileTypeSniffer.Sniff(TestFiles.Text(), fileName));

    [Fact]
    public void RejectsTextWithBinaryBytes()
    {
        Assert.Null(FileTypeSniffer.Sniff([0x68, 0x69, 0x00, 0x21], "notes.txt"));
        Assert.Null(FileTypeSniffer.Sniff([0x68, 0x69, 0x07, 0x21], "notes.txt"));
    }

    [Fact]
    public void RejectsTextThatIsNotValidUtf8() =>
        Assert.Null(FileTypeSniffer.Sniff([0x68, 0x69, 0xC3, 0x28], "notes.txt"));

    [Fact]
    public void AcceptsUtf8TextWithABomAndLineBreaks()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("Caf\u00e9\r\n\tIndented\f")).ToArray();

        Assert.Equal("text/plain", FileTypeSniffer.Sniff(bytes, "notes.txt")?.ContentType);
    }

    [Fact]
    public void IgnoresTheDeclaredContentType_OnlyBytesAndExtensionCount()
    {
        Assert.Null(FileTypeSniffer.Sniff(TestFiles.Exe(), "photo.jpg"));
    }
}
