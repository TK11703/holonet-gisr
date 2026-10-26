using Bunit;
using Gisr.Web.Layout;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Gisr.Web.Tests.Layout;

public class SideNavTests : BunitContext
{
    public SideNavTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddScoped<ShellInterop>();
    }

    [Fact]
    public void RendersGroupedPrimaryLinks()
    {
        var cut = Render<SideNav>();

        Assert.Equal("Primary", cut.Find("nav").GetAttribute("aria-label"));
        var headings = cut.FindAll(".sidenav-heading").Select(h => h.TextContent.Trim()).ToArray();
        Assert.Equal(["Records", "Find", "Account"], headings);

        var links = cut.FindAll("a.sidenav-link").ToDictionary(a => a.TextContent.Trim(), a => a.GetAttribute("href"));
        Assert.Equal("/records", links["Citizen Records"]);
        Assert.Equal("/gallery", links["Gallery"]);
        Assert.Equal("/search", links["Search"]);
        Assert.Equal("/account", links["Account Settings"]);
    }

    [Fact]
    public void ActiveLink_HasAriaCurrentPage()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("/gallery");

        var cut = Render<SideNav>();

        var current = cut.FindAll("a[aria-current=page]");
        Assert.Single(current);
        Assert.Equal("/gallery", current[0].GetAttribute("href"));
    }

    [Fact]
    public void NestedRoute_KeepsParentLinkActive()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("/records/new");

        var cut = Render<SideNav>();

        Assert.Equal("/records", cut.Find("a[aria-current=page]").GetAttribute("href"));
    }

    [Fact]
    public void WhenOpen_ShowsBackdrop_AndCloseButtonRaisesCallback()
    {
        bool? changed = null;
        var cut = Render<SideNav>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.IsOpenChanged, EventCallback.Factory.Create<bool>(this, v => changed = v)));

        Assert.Contains("open", cut.Find("nav").ClassList);
        Assert.NotNull(cut.Find(".sidenav-backdrop"));

        cut.Find("button.sidenav-close").Click();

        Assert.False(changed);
    }

    [Fact]
    public void WhenClosed_HasNoBackdrop()
    {
        var cut = Render<SideNav>();

        Assert.DoesNotContain("open", cut.Find("nav").ClassList);
        Assert.Empty(cut.FindAll(".sidenav-backdrop"));
    }
}
