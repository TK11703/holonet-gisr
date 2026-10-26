using Bunit;
using Gisr.Web.Layout;

namespace Gisr.Web.Tests.Layout;

public class PageHeaderTests : BunitContext
{
    [Fact]
    public void RendersTitleHeading_ThenBreadcrumbNav()
    {
        var cut = Render<PageHeader>(p => p
            .Add(c => c.Title, "Citizen Records")
            .Add(c => c.Crumbs, [new Crumb("Home", "/"), new Crumb("Citizen Records")]));

        var header = cut.Find(".page-header");
        var children = header.Children.ToArray();
        Assert.Equal("H1", children[0].TagName);
        Assert.Equal("Citizen Records", children[0].TextContent.Trim());
        Assert.Equal("NAV", children[1].TagName);
        Assert.Equal("breadcrumb", children[1].GetAttribute("aria-label"));
    }

    [Fact]
    public void LastCrumb_IsCurrentPage_AndEarlierCrumbsAreLinks()
    {
        var cut = Render<PageHeader>(p => p
            .Add(c => c.Title, "Edit")
            .Add(c => c.Crumbs, [new Crumb("Home", "/"), new Crumb("Records", "/records"), new Crumb("Edit")]));

        var items = cut.FindAll("li.breadcrumb-item");
        Assert.Equal(3, items.Count);
        Assert.Equal("/records", items[1].QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal("page", items[2].GetAttribute("aria-current"));
        Assert.Null(items[2].QuerySelector("a"));
    }

    [Fact]
    public void WithoutCrumbs_RendersNoNav()
    {
        var cut = Render<PageHeader>(p => p.Add(c => c.Title, "Search"));

        Assert.Empty(cut.FindAll("nav"));
    }
}
