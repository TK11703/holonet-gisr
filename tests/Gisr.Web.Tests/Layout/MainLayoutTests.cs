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

        Assert.Contains("<html lang=\"en\" data-bs-theme=\"dark\">", app, StringComparison.Ordinal);
        Assert.Contains("theme.css", app, StringComparison.Ordinal);
    }

    [Fact]
    public void ThemeCss_DisablesMotion_ForReducedMotionUsers()
    {
        var css = File.ReadAllText(RepoFile("src", "Gisr.Web", "wwwroot", "theme.css"));

        Assert.Contains("prefers-reduced-motion", css, StringComparison.Ordinal);
    }
}
