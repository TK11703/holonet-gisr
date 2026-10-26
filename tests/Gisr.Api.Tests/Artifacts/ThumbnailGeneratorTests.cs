using Gisr.Api.Artifacts;
using SkiaSharp;

namespace Gisr.Api.Tests.Artifacts;

public class ThumbnailGeneratorTests
{
    private static (SKEncodedImageFormat? Format, int Width, int Height) Inspect(byte[] bytes)
    {
        using var codec = SKCodec.Create(new MemoryStream(bytes));
        return (codec?.EncodedFormat, codec?.Info.Width ?? 0, codec?.Info.Height ?? 0);
    }

    [Fact]
    public void ALandscapeImage_IsScaledToA320PixelLongestEdge_AsJpeg()
    {
        var thumbnail = ThumbnailGenerator.Create(TestFiles.Png(1000, 500));

        Assert.NotNull(thumbnail);
        Assert.Equal((SKEncodedImageFormat.Jpeg, 320, 160), Inspect(thumbnail));
    }

    [Fact]
    public void APortraitImage_IsScaledByItsHeight()
    {
        var thumbnail = ThumbnailGenerator.Create(TestFiles.Jpeg(400, 800));

        Assert.Equal((SKEncodedImageFormat.Jpeg, 160, 320), Inspect(thumbnail!));
    }

    [Fact]
    public void ASmallImage_IsNotUpscaled()
    {
        var thumbnail = ThumbnailGenerator.Create(TestFiles.Png(100, 50));

        Assert.Equal((SKEncodedImageFormat.Jpeg, 100, 50), Inspect(thumbnail!));
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void EveryAllowedImageFormat_ProducesAJpegThumbnail(byte[] image)
    {
        var thumbnail = ThumbnailGenerator.Create(image);

        Assert.NotNull(thumbnail);
        Assert.Equal(SKEncodedImageFormat.Jpeg, Inspect(thumbnail).Format);
    }

    public static TheoryData<byte[]> Formats => new()
    {
        TestFiles.Png(800, 600),
        TestFiles.Jpeg(800, 600),
        TestFiles.WebP(800, 600),
        TestFiles.Gif(),
    };

    [Fact]
    public void ACorruptImage_IsRejected()
    {
        var corrupt = TestFiles.Png(400, 400);
        Array.Fill(corrupt, (byte)0x7F, 40, corrupt.Length - 40);

        Assert.Null(ThumbnailGenerator.Create(corrupt));
    }

    [Fact]
    public void BytesThatAreNotAnImage_AreRejected()
    {
        Assert.Null(ThumbnailGenerator.Create(TestFiles.Text("not an image at all")));
        Assert.Null(ThumbnailGenerator.Create([]));
    }

    [Fact]
    public void AnImageWithAnAbsurdPixelCount_IsRejected_BeforeItIsDecoded()
    {
        Assert.Null(ThumbnailGenerator.Create(PngHeaderOnly(50_000, 50_000)));
    }

    // A PNG whose header claims huge dimensions; no pixel data is needed because the size check comes first.
    private static byte[] PngHeaderOnly(int width, int height)
    {
        using var stream = new MemoryStream();
        stream.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = 0;
        WriteChunk(stream, "IHDR", header);
        WriteChunk(stream, "IDAT", [0x78, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01]);
        WriteChunk(stream, "IEND", []);
        return stream.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        Span<byte> length = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);
        stream.Write(typeBytes);
        stream.Write(data);
        var crc = Crc32([.. typeBytes, .. data]);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(length, crc);
        stream.Write(length);
    }

    private static uint Crc32(byte[] bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }
}
