using Bunit;
using Gisr.Web.Help;

namespace Gisr.Web.Tests.Help;

public class AboutTests : BunitContext
{
    [Fact]
    public void RendersSingleTitle_AndBreadcrumb()
    {
        var cut = Render<About>();

        Assert.Equal("About GISR", cut.Find("h1").TextContent.Trim());
        Assert.Single(cut.FindAll("h1"));
        Assert.Equal(["Home", "Help", "About"],
            cut.FindAll("nav[aria-label=breadcrumb] li").Select(item => item.TextContent.Trim()));
    }

    [Fact]
    public void RolesTable_ComparesCitizensAndOfficers_WithTextNotIcons()
    {
        var cut = Render<About>();

        Assert.Equal(["Capability", "Citizen", "Officer"],
            cut.FindAll("thead th").Select(header => header.TextContent.Trim()));
        Assert.All(cut.FindAll("tbody tr"), row =>
            Assert.All(row.QuerySelectorAll("td"), cell => Assert.False(string.IsNullOrWhiteSpace(cell.TextContent))));
    }

    [Theory]
    [InlineData("/records")]
    [InlineData("/gallery")]
    [InlineData("/search")]
    [InlineData("/account")]
    [InlineData("/faq")]
    public void LinksToExistingFeatures(string href) =>
        Assert.NotEmpty(Render<About>().FindAll($"a[href='{href}']"));

    [Fact]
    public void EverySectionHeading_LabelsItsSection()
    {
        var cut = Render<About>();

        foreach (var section in cut.FindAll("section[aria-labelledby]"))
        {
            Assert.NotNull(cut.Find($"#{section.GetAttribute("aria-labelledby")}"));
        }
    }
}
