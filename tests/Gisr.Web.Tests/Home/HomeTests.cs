using Bunit;
using HomePage = Gisr.Web.Home.Home;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Gisr.Web.Tests.Home;

public class HomeTests : BunitContext
{
    [Fact]
    public void Root_RemainsHome_WithoutRequiringApiServices()
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        var originalUri = navigation.Uri;

        var cut = Render<HomePage>();

        Assert.Equal(originalUri, navigation.Uri);
        Assert.Equal("Galactic Identity and Service Records", cut.Find("h1").TextContent.Trim());
        Assert.Single(cut.FindAll("h1"));
    }

    [Fact]
    public void ExplainsHierarchy_WithoutInventedParentLinks()
    {
        var cut = Render<HomePage>();
        var hierarchy = cut.Find("[aria-label='System hierarchy']");

        Assert.Equal(["HoloNet", "Databank", "GISR"],
            hierarchy.QuerySelectorAll("li").Select(item => item.TextContent.Trim()));
        Assert.Empty(hierarchy.QuerySelectorAll("a"));
    }

    [Theory]
    [InlineData("/records", "Browse records")]
    [InlineData("/records/new", "New record")]
    [InlineData("/search", "Search the archive")]
    [InlineData("/gallery", "Explore gallery")]
    public void PrimaryTasks_LinkToExistingFeatures(string href, string label)
    {
        var cut = Render<HomePage>();

        Assert.Contains(cut.FindAll($"a[href='{href}']"),
            link => link.TextContent.Trim().StartsWith(label, StringComparison.Ordinal));
    }

    [Fact]
    public void Overview_CoversIdentityServiceAndEvidence()
    {
        var cut = Render<HomePage>();
        var overview = cut.Find("[aria-labelledby='archive-overview']");

        Assert.Contains("Identity & affiliation", overview.TextContent);
        Assert.Contains("Service & distinction", overview.TextContent);
        Assert.Contains("Evidence & provenance", overview.TextContent);
        Assert.Contains("Jedi", cut.Markup);
        Assert.Contains("bounty hunters", cut.Markup);
    }

    [Fact]
    public void Seal_IsDecorativeBesideProductName_AndHasExplicitDimensions()
    {
        var cut = Render<HomePage>();
        var image = cut.Find("img[src='gisr-mark.svg']");

        Assert.Equal(string.Empty, image.GetAttribute("alt"));
        Assert.NotNull(image.GetAttribute("width"));
        Assert.NotNull(image.GetAttribute("height"));
    }
}
