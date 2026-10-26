using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Gisr.Api.Tests.Common;
using Gisr.Api.Tests.Records;
using Gisr.Shared.Artifacts;
using Gisr.Shared.Records;
using SkiaSharp;

namespace Gisr.Api.Tests.Artifacts;

internal static class TestFiles
{
    public static byte[] Png(int width = 64, int height = 48) => Encode(SKEncodedImageFormat.Png, width, height);

    public static byte[] Jpeg(int width = 64, int height = 48) => Encode(SKEncodedImageFormat.Jpeg, width, height);

    public static byte[] WebP(int width = 64, int height = 48) => Encode(SKEncodedImageFormat.Webp, width, height);

    // Skia cannot encode GIF, so this is a hand-made 1x1 image.
    public static byte[] Gif() => Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");

    public static byte[] Pdf(string text = "Scanned orders") =>
        Encoding.ASCII.GetBytes($"%PDF-1.4\n1 0 obj<</Type/Catalog>>endobj\n% {text}\ntrailer<</Root 1 0 R>>\n%%EOF\n");

    public static byte[] Docx() => Zip("word/document.xml", "[Content_Types].xml");

    public static byte[] Zip(params string[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write("<x/>");
            }
        }

        return stream.ToArray();
    }

    public static byte[] Text(string text = "Plain notes about the Clone Wars.") => Encoding.UTF8.GetBytes(text);

    public static byte[] Exe() => [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0xFF, 0xFF];

    private static byte[] Encode(SKEncodedImageFormat format, int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.SteelBlue);
            using var paint = new SKPaint { Color = SKColors.Gold };
            canvas.DrawCircle(width / 2f, height / 2f, Math.Min(width, height) / 4f, paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }
}

internal static class ArtifactApi
{
    public static MultipartFormDataContent Form(byte[] bytes, string fileName, string? description = null)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        if (description is not null)
        {
            form.Add(new StringContent(description), "description");
        }

        return form;
    }

    public static Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid recordId, byte[] bytes, string fileName, string? description = null) =>
        client.PostAsync($"/api/records/{recordId}/artifacts", Form(bytes, fileName, description));

    public static async Task<ArtifactDto> UploadOkAsync(HttpClient client, Guid recordId, byte[] bytes, string fileName, string? description = null)
    {
        var response = await UploadAsync(client, recordId, bytes, fileName, description);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ArtifactDto>())!;
    }

    public static async Task<RecordDto> CreateOwnRecordAsync(HttpClient client, string title = "My service")
    {
        var response = await client.PostAsJsonAsync("/api/records", SealedTestData.Request(null, title, sealedRecord: false, code: null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RecordDto>())!;
    }

    public static HttpRequestMessage Get(string url, string? token = null) => SealedTestData.WithToken(HttpMethod.Get, url, token);
}
