using Bunit;
using Gisr.Web.Layout;
using Gisr.Web.Tests.Common;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Gisr.Web.Tests.Layout;

public class MainLayoutTests : BunitContext
{
    public MainLayoutTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddScoped<ShellInterop>();
        new FakeApi().OnJson(HttpMethod.Get, "api/me", new Gisr.Shared.Accounts.AccountDto(
            Guid.NewGuid(), "Test User", null, null, true, false, DateTimeOffset.UtcNow)).AddTo(Services);
    }

    private static string RepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Gisr.slnx")))
        {
            directory = directory.Parent;
        }

        return Path.Combine([directory!.FullName, .. parts]);
    }

    [Fact]
    public void RendersLandmarks_WithSkipLinkFirst()
    {
        var cut = Render<MainLayout>(p => p.Add(c => c.Body, (RenderFragment)(b => b.AddMarkupContent(0, "<p id=\"body\">hi</p>"))));

        Assert.NotNull(cut.Find("header"));
        Assert.NotNull(cut.Find("nav"));
        Assert.NotNull(cut.Find("main#main-content p#body"));
        var firstLink = cut.Find("a");
        Assert.Equal("#main-content", firstLink.GetAttribute("href"));
        Assert.Contains("Skip to content", firstLink.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void HasNoLightOrDarkSwitch()
    {
        var cut = Render<MainLayout>();

        Assert.Empty(cut.FindAll("[role=switch], input[type=checkbox]"));
        Assert.DoesNotContain("theme", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("light mode", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AppShell_DeclaresDarkTheme_OnHtmlElement()
    {
        var app = File.ReadAllText(RepoFile("src", "Gisr.Web", "Components", "App.razor"));

        Assert.Contains("<html lang=\"en\" data-bs-theme=\"dark\" data-gisr-theme=\"@_theme\">", app, StringComparison.Ordinal);
        Assert.Contains("theme.css", app, StringComparison.Ordinal);
    }

    [Fact]
    public void AppShell_FallsBackToTheDefaultTheme_WhenTheSavedOneIsUnknown()
    {
        var app = File.ReadAllText(RepoFile("src", "Gisr.Web", "Components", "App.razor"));

        Assert.Contains("private string _theme = SiteThemes.Default;", app, StringComparison.Ordinal);
        Assert.Contains("SiteThemes.IsKnown(account.Theme)", app, StringComparison.Ordinal);
    }

    [Fact]
    public void ThemeCss_StylesEveryThemeOffered()
    {
        var css = File.ReadAllText(RepoFile("src", "Gisr.Web", "wwwroot", "theme.css"));

        foreach (var theme in Gisr.Shared.Accounts.SiteThemes.All.Where(t => t.Id != Gisr.Shared.Accounts.SiteThemes.Default))
        {
            Assert.Contains($":root[data-gisr-theme=\"{theme.Id}\"]", css, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ThemeCss_StylesSelectPickers_WithThemeColors()
    {
        var css = File.ReadAllText(RepoFile("src", "Gisr.Web", "wwwroot", "theme.css"));

        Assert.Contains("@supports (appearance: base-select)", css, StringComparison.Ordinal);
        Assert.Contains(".form-select::picker(select)", css, StringComparison.Ordinal);
        Assert.Contains("border: 1px solid var(--gisr-border)", css, StringComparison.Ordinal);
        Assert.Contains(".form-select option:hover", css, StringComparison.Ordinal);
        Assert.Contains(".form-select option:checked", css, StringComparison.Ordinal);
        Assert.Contains("box-shadow: 0 0 0 0.2rem var(--bs-focus-ring-color)", css, StringComparison.Ordinal);
    }

    [Fact]
    public void ThemeCss_DisablesMotion_ForReducedMotionUsers()
    {
        var css = File.ReadAllText(RepoFile("src", "Gisr.Web", "wwwroot", "theme.css"));

        Assert.Contains("prefers-reduced-motion", css, StringComparison.Ordinal);
    }
}
