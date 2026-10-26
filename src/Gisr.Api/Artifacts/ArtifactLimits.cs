namespace Gisr.Api.Artifacts;

public static class ArtifactLimits
{
    public const long MaxBytes = 26_214_400;
    public const int MaxActivePerRecord = 50;
    public const int MaxFileNameLength = 255;
    public const int MaxDescriptionLength = 500;
}

public static class ArtifactFileName
{
    private const string Forbidden = "\"<>:|?*";

    // Keeps only the final path segment and drops control and reserved characters; the name is display-only, never a storage path.
    public static string Sanitize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var name = raw[(raw.LastIndexOfAny(['/', '\\']) + 1)..];
        name = new string([.. name.Where(c => !char.IsControl(c) && !Forbidden.Contains(c, StringComparison.Ordinal))]).Trim();
        if (name is "." or "..")
        {
            return string.Empty;
        }

        if (name.Length <= ArtifactLimits.MaxFileNameLength)
        {
            return name;
        }

        var extension = Path.GetExtension(name);
        return extension.Length is > 0 and < 20
            ? name[..(ArtifactLimits.MaxFileNameLength - extension.Length)] + extension
            : name[..ArtifactLimits.MaxFileNameLength];
    }
}
