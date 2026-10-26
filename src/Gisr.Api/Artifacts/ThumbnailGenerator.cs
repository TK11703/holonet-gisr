using SkiaSharp;

namespace Gisr.Api.Artifacts;

public static class ThumbnailGenerator
{
    public const int MaxEdge = 320;

    private const long MaxPixels = 100_000_000;
    private const int Quality = 80;

    // Scales down only; returns null for anything that is not a decodable image or that would need an absurd amount of memory.
    public static byte[]? Create(byte[] image)
    {
        if (image.Length == 0)
        {
            return null;
        }

        try
        {
            using var codec = SKCodec.Create(new MemoryStream(image, writable: false));
            if (codec is null)
            {
                return null;
            }

            var info = codec.Info;
            if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > MaxPixels)
            {
                return null;
            }

            using var bitmap = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
            if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
            {
                return null;
            }

            return Render(bitmap, codec.EncodedOrigin);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OutOfMemoryException or NullReferenceException)
        {
            return null;
        }
    }

    private static byte[]? Render(SKBitmap bitmap, SKEncodedOrigin origin)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var orientedWidth = swap ? height : width;
        var orientedHeight = swap ? width : height;
        var scale = Math.Min(1f, (float)MaxEdge / Math.Max(orientedWidth, orientedHeight));
        var targetWidth = Math.Max(1, (int)Math.Round(orientedWidth * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(orientedHeight * scale));

        // x' = a*x + b*y + tx, y' = c*x + d*y + ty maps the stored pixels onto the upright image.
        var (a, b, tx, c, d, ty) = origin switch
        {
            SKEncodedOrigin.TopRight => (-1f, 0f, (float)width, 0f, 1f, 0f),
            SKEncodedOrigin.BottomRight => (-1f, 0f, (float)width, 0f, -1f, (float)height),
            SKEncodedOrigin.BottomLeft => (1f, 0f, 0f, 0f, -1f, (float)height),
            SKEncodedOrigin.LeftTop => (0f, 1f, 0f, 1f, 0f, 0f),
            SKEncodedOrigin.RightTop => (0f, -1f, (float)height, 1f, 0f, 0f),
            SKEncodedOrigin.RightBottom => (0f, -1f, (float)height, -1f, 0f, (float)width),
            SKEncodedOrigin.LeftBottom => (0f, 1f, 0f, -1f, 0f, (float)width),
            _ => (1f, 0f, 0f, 0f, 1f, 0f),
        };

        using var surface = SKSurface.Create(new SKImageInfo(targetWidth, targetHeight, SKColorType.Rgba8888, SKAlphaType.Opaque));
        if (surface is null)
        {
            return null;
        }

        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        canvas.SetMatrix(new SKMatrix(scale * a, scale * b, scale * tx, scale * c, scale * d, scale * ty, 0, 0, 1));
        using var source = SKImage.FromBitmap(bitmap);
        canvas.DrawImage(source, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));

        using var snapshot = surface.Snapshot();
        using var data = snapshot.Encode(SKEncodedImageFormat.Jpeg, Quality);
        return data?.ToArray();
    }
}
