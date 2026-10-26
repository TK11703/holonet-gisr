using System.Globalization;
using Gisr.Shared.Artifacts;

namespace Gisr.Web.Artifacts;

public static class ArtifactDisplay
{
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".webp", ".gif"];

    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => (bytes / 1024d).ToString("F1", CultureInfo.InvariantCulture) + " KB",
        _ => (bytes / (1024d * 1024d)).ToString("F1", CultureInfo.InvariantCulture) + " MB",
    };

    public static bool IsImageName(string fileName) =>
        ImageExtensions.Contains(Path.GetExtension(fileName).ToLowerInvariant(), StringComparer.Ordinal);

    // The `v` value is a per-viewing id, not a secret; the proxy maps it to the token it holds for the signed-in user.
    public static string MediaUrl(Guid artifactId, bool thumbnail, Guid? viewingId) =>
        $"/media/artifacts/{artifactId}{(thumbnail ? "/thumbnail" : string.Empty)}{(viewingId is { } v ? $"?v={v}" : string.Empty)}";

    public static string? SearchBadge(ArtifactDto artifact) => artifact.IndexStatus switch
    {
        "skipped" or "failed" => "Not searchable",
        "pending" => "Indexing pending",
        _ => null,
    };

    public static string? SearchReason(ArtifactDto artifact) => artifact.IndexDetail switch
    {
        "unsupported_type" => artifact.IsImage ? "Images are not searched by content." : "This file type is not searched.",
        "no_text" => "No readable text was found.",
        "ocr_disabled" => "Text recognition for scanned files is turned off.",
        "ocr_budget_exhausted" => "The monthly text-recognition budget is used up; it is retried next month.",
        "ocr_page_limit" => "Only the first 100 pages were read.",
        "parse_error" => "The file could not be read.",
        "too_large" => "The file is too large to index.",
        _ => artifact.IndexStatus == "failed" ? "Indexing failed." : null,
    };
}
