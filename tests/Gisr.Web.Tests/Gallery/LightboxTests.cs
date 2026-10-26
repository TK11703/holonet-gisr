using Bunit;
using Gisr.Shared.Artifacts;
using Gisr.Web.Gallery;
using Gisr.Web.Layout;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Gisr.Web.Tests.Gallery;

public class LightboxTests : BunitContext
{
    private readonly Guid _recordId = Guid.NewGuid();
    private readonly List<int> _indexChanges = [];
    private int _closed;

    public LightboxTests()
    {
        Services.AddScoped<ShellInterop>();
    }

    private static ArtifactDto Image(string name, string? description) =>
        new(Guid.NewGuid(), Guid.Empty, name, "image/png", 100, true, description, "Rex", DateTimeOffset.UtcNow, "skipped", "unsupported_type");

    private IReadOnlyList<ArtifactDto> Items { get; } = [Image("a.png", "First view"), Image("b.png", "Second view"), Image("c.png", "Third view")];

    private IRenderedComponent<Lightbox> RenderLightbox(int index = 0) =>
        Render<Lightbox>(p =>
        {
            p.Add(c => c.Items, Items);
            p.Add(c => c.Index, index);
            p.Add(c => c.RecordId, _recordId);
            p.Add(c => c.IndexChanged, EventCallback.Factory.Create<int>(this, i => _indexChanges.Add(i)));
            p.Add(c => c.OnClose, EventCallback.Factory.Create(this, () => _closed++));
        });

    private BunitJSModuleInterop SetupTrap()
    {
        var module = JSInterop.SetupModule("./js/focus-trap.js");
        module.Setup<int>("trap", _ => true).SetResult(7);
        module.SetupVoid("release", _ => true).SetVoidResult();
        return module;
    }

    [Fact]
    public void ShowsTheLargeImage_WithTheDescriptionAsAltText_AndACaption()
    {
        SetupTrap();

        var cut = RenderLightbox(1);

        var image = cut.Find("img.lightbox-image");
        Assert.Equal($"/media/artifacts/{Items[1].Id}", image.GetAttribute("src"));
        Assert.Equal("Second view", image.GetAttribute("alt"));
        Assert.Equal("Second view", cut.Find("figcaption").TextContent.Trim());
        Assert.Contains("2 of 3", cut.Find(".lightbox-counter").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void AnImageWithoutADescription_FallsBackToItsFileName()
    {
        SetupTrap();
        var items = new[] { Image("fallback.png", null) };

        var cut = Render<Lightbox>(p =>
        {
            p.Add(c => c.Items, items);
            p.Add(c => c.Index, 0);
            p.Add(c => c.RecordId, _recordId);
        });

        Assert.Equal("fallback.png", cut.Find("img.lightbox-image").GetAttribute("alt"));
    }

    [Fact]
    public void IsADialog_WithAnAccessibleName_AndOffersTheOpenRecordLink()
    {
        SetupTrap();

        var cut = RenderLightbox();

        var dialog = cut.Find("[role=dialog]");
        Assert.Equal("true", dialog.GetAttribute("aria-modal"));
        Assert.Equal("Image viewer", dialog.GetAttribute("aria-label"));
        var link = cut.Find("a.lightbox-open-record");
        Assert.Equal($"/records/{_recordId}", link.GetAttribute("href"));
        Assert.Equal("Open record", link.TextContent.Trim());
    }

    [Fact]
    public void Next_AndPrevious_MoveThroughTheImages_AndStopAtTheEnds()
    {
        SetupTrap();
        var cut = RenderLightbox(0);

        Assert.True(cut.Find("#lightbox-previous").HasAttribute("disabled"));
        cut.Find("#lightbox-next").Click();
        Assert.Equal([1], _indexChanges);
        Assert.Equal("Next image", cut.Find("#lightbox-next").GetAttribute("aria-label"));
        Assert.Equal("Previous image", cut.Find("#lightbox-previous").GetAttribute("aria-label"));

        var last = RenderLightbox(2);
        Assert.True(last.Find("#lightbox-next").HasAttribute("disabled"));
        last.Find("#lightbox-previous").Click();
        Assert.Equal([1, 1], _indexChanges);
    }

    [Fact]
    public void ArrowKeys_Navigate()
    {
        SetupTrap();
        var cut = RenderLightbox(1);

        cut.Find("[role=dialog]").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        cut.Find("[role=dialog]").KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });

        Assert.Equal([2, 0], _indexChanges);
    }

    [Fact]
    public void ArrowKeys_DoNothingPastTheEnds()
    {
        SetupTrap();
        var cut = RenderLightbox(0);

        cut.Find("[role=dialog]").KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });

        Assert.Empty(_indexChanges);
    }

    [Fact]
    public void TheCloseButton_AndTheBackdrop_Close()
    {
        SetupTrap();
        var cut = RenderLightbox();

        Assert.Equal("Close image viewer", cut.Find("#lightbox-close").GetAttribute("aria-label"));
        cut.Find("#lightbox-close").Click();
        cut.Find(".lightbox-backdrop").Click();

        Assert.Equal(2, _closed);
    }

    [Fact]
    public async Task Escape_Closes_ThroughTheFocusTrap()
    {
        var module = SetupTrap();
        var cut = RenderLightbox();

        var reference = (DotNetObjectReference<ShellInterop.EscapeCallback>)module.Invocations["trap"].Single().Arguments[1]!;
        await cut.InvokeAsync(() => reference.Value.OnEscape());

        Assert.Equal(1, _closed);
        module.VerifyInvoke("trap");
    }

    [Fact]
    public async Task FocusIsTrappedOnOpen_AndReleasedOnClose()
    {
        var module = SetupTrap();
        var cut = RenderLightbox();
        module.VerifyInvoke("trap", 1);
        Assert.Empty(module.Invocations["release"]);

        await DisposeComponentsAsync();

        var release = Assert.Single(module.Invocations["release"]);
        Assert.Equal(7, release.Arguments[0]);
        Assert.NotNull(cut);
    }

    [Fact]
    public void TheLightboxFade_IsAnAnimation_ThatReducedMotionTurnsOff()
    {
        var css = File.ReadAllText(FindThemeCss());

        Assert.Contains("animation: lightbox-fade", css, StringComparison.Ordinal);
        var block = css[css.IndexOf("@media (prefers-reduced-motion: reduce)", StringComparison.Ordinal)..];
        Assert.Contains("*,", block, StringComparison.Ordinal);
        Assert.Contains("animation: none !important", block, StringComparison.Ordinal);
    }

    private static string FindThemeCss()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Gisr.Web", "wwwroot", "theme.css");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("theme.css was not found above the test output directory.");
    }
}
