using Bunit;
using Gisr.Web.Layout;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Gisr.Web.Tests.Layout;

public class TopBarTests : BunitContext
{
    public TopBarTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddScoped<ShellInterop>();
    }

    [Fact]
    public void SiteBrand_HasSealAndAccessibleName_AndLinksHome()
    {
        var cut = Render<TopBar>();

        var brand = cut.Find("a.topbar-brand");
        Assert.Equal("GISR", brand.TextContent.Trim());
        Assert.Equal("/", brand.GetAttribute("href"));
        Assert.Equal("GISR home", brand.GetAttribute("aria-label"));
        var image = brand.QuerySelector("img");
        Assert.NotNull(image);
        Assert.Equal("gisr-mark.svg", image.GetAttribute("src"));
        Assert.Equal(string.Empty, image.GetAttribute("alt"));
    }

    [Fact]
    public void SearchBox_HasAccessibleName_AndNavigatesOnSubmit()
    {
        var cut = Render<TopBar>();

        var input = cut.Find("input[type=search]");
        var label = cut.Find($"label[for={input.Id}]");
        Assert.Equal("Search", label.TextContent.Trim());

        input.Input("rebel pilots");
        cut.Find("form[role=search]").Submit();

        var navigation = Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith("/search?q=rebel%20pilots", navigation.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchBox_DoesNotNavigate_WhenEmpty()
    {
        var cut = Render<TopBar>();
        var navigation = Services.GetRequiredService<NavigationManager>();
        var before = navigation.Uri;

        cut.Find("input[type=search]").Input("   ");
        cut.Find("form[role=search]").Submit();

        Assert.Equal(before, navigation.Uri);
    }

    [Fact]
    public void NewRecordButton_IsIconOnly_WithTitleAndAccessibleName_AndLinksToNewRecordPage()
    {
        var cut = Render<TopBar>();

        var link = cut.Find("a.btn-primary");
        Assert.Equal(string.Empty, link.TextContent.Trim());
        Assert.Equal("New record", link.GetAttribute("title"));
        Assert.Equal("New record", link.GetAttribute("aria-label"));
        Assert.Contains("btn-icon", link.ClassList);
        Assert.Single(link.QuerySelectorAll("svg"));
        Assert.Equal("/records/new", link.GetAttribute("href"));
    }

    [Fact]
    public void MenuButton_HasAccessibleName_AndRaisesCallback()
    {
        var clicked = false;
        var cut = Render<TopBar>(p => p.Add(c => c.OnMenuClick, EventCallback.Factory.Create(this, () => clicked = true)));

        var button = cut.Find("button[aria-controls=side-nav]");
        Assert.Equal("Open navigation", button.GetAttribute("aria-label"));
        button.Click();

        Assert.True(clicked);
    }

    [Fact]
    public void RendersNotificationsAndProfileSlots_AroundNewRecordButton()
    {
        var cut = Render<TopBar>(p => p
            .Add(c => c.Notifications, "<span id=\"slot-notifications\"></span>")
            .Add(c => c.Profile, "<span id=\"slot-profile\"></span>"));

        var html = cut.Markup;
        Assert.True(html.IndexOf("slot-notifications", StringComparison.Ordinal) < html.IndexOf("New record", StringComparison.Ordinal));
        Assert.True(html.IndexOf("New record", StringComparison.Ordinal) < html.IndexOf("slot-profile", StringComparison.Ordinal));
    }

    [Fact]
    public void HasNoThemeToggle()
    {
        var cut = Render<TopBar>();

        Assert.DoesNotContain("theme", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }
}
