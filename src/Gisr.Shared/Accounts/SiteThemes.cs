namespace Gisr.Shared.Accounts;

public sealed record SiteThemeOption(string Id, string Name);

// Ids are stored on the account and written to the page's data-gisr-theme attribute; theme.css styles each one.
public static class SiteThemes
{
    public const string Teal = "teal";
    public const string Hub = "hub";
    public const string Navy = "navy";
    public const string Grey = "grey";
    public const string Default = Teal;

    public static IReadOnlyList<SiteThemeOption> All { get; } =
    [
        new(Teal, "Green / Teal"),
        new(Hub, "GISR Hub"),
        new(Navy, "Dark Navy"),
        new(Grey, "Dark Grey"),
    ];

    public static bool IsKnown(string? id) => All.Any(option => option.Id == id);
}
