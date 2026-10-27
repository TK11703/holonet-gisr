using System.Net;
using Bunit;
using Gisr.Shared.Accounts;
using Gisr.Shared.Common;
using Gisr.Shared.Gallery;
using Gisr.Web.Gallery;
using Gisr.Web.Tests.Common;
using Gisr.Web.Tests.Records;

namespace Gisr.Web.Tests.Gallery;

public class GalleryIndexTests : BunitContext
{
    private const string Default = "api/gallery?page=1&pageSize=12";

    private readonly FakeApi _api = new();

    public GalleryIndexTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _api.OnJson(HttpMethod.Get, "api/me/affiliations", Samples.Affiliations);
        _api.AddTo(Services);
    }

    private static GalleryGroupDto Group(string title = "Clone Wars service", int images = 2, string affiliation = "Military", int covers = -1) =>
        new(Guid.NewGuid(), title, affiliation, "Rex", images, [.. Enumerable.Range(0, covers < 0 ? Math.Min(images, 4) : covers).Select(_ => Guid.NewGuid())]);

    private static PagedResult<GalleryGroupDto> Page(long total, params GalleryGroupDto[] items) => new(1, 12, total, items);

    private void SetOfficer(bool officer) =>
        _api.OnJson(HttpMethod.Get, "api/me", new AccountDto(Guid.NewGuid(), "Someone", null, null, true, officer, DateTimeOffset.UtcNow));

    [Fact]
    public void RendersTheTitleAndBreadcrumb()
    {
        _api.OnJson(HttpMethod.Get, Default, Page(1, Group()));

        var cut = Render<GalleryIndex>();

        Assert.Equal("Gallery", cut.Find("h1").TextContent.Trim());
        Assert.NotNull(cut.Find("nav[aria-label=breadcrumb]"));
    }

    [Fact]
    public void ShowsOneCardPerRecord_WithTitleAffiliationAndImageCount()
    {
        var group = Group("Temple guard", images: 5, affiliation: "Jedi");
        _api.OnJson(HttpMethod.Get, Default, Page(2, group, Group("Clone Wars", images: 1)));

        var cut = Render<GalleryIndex>();

        var cards = cut.FindAll(".gallery-card");
        Assert.Equal(2, cards.Count);
        var first = cards[0];
        var link = first.QuerySelector("a.gallery-card-link")!;
        Assert.Equal($"/gallery/{group.RecordId}", link.GetAttribute("href"));
        Assert.Equal("Temple guard", link.TextContent.Trim());
        Assert.Contains("Jedi", first.TextContent, StringComparison.Ordinal);
        Assert.Contains("5 images", first.TextContent, StringComparison.Ordinal);
        Assert.Contains("1 image", cards[1].TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("1 images", cards[1].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void ACard_ShowsUpToFourCoverThumbnails_ThroughTheMediaProxy()
    {
        var group = Group(images: 9);
        _api.OnJson(HttpMethod.Get, Default, Page(1, group));

        var cut = Render<GalleryIndex>();

        var thumbnails = cut.FindAll(".gallery-card img");
        Assert.Equal(4, thumbnails.Count);
        Assert.Equal(group.CoverArtifactIds.Select(id => $"/media/artifacts/{id}/thumbnail"), thumbnails.Select(t => t.GetAttribute("src")));
        Assert.All(thumbnails, t => Assert.Equal(string.Empty, t.GetAttribute("alt")));
    }

    [Fact]
    public void TheWholeCardTitle_IsTheOnlyLink_SoScreenReadersHearItOnce()
    {
        _api.OnJson(HttpMethod.Get, Default, Page(1, Group()));

        var cut = Render<GalleryIndex>();

        Assert.Single(cut.FindAll(".gallery-card a"));
    }

    [Fact]
    public void WithNoImages_AnEmptyStateExplainsHowToAddThem()
    {
        _api.OnJson(HttpMethod.Get, Default, Page(0));

        var cut = Render<GalleryIndex>();

        Assert.Empty(cut.FindAll(".gallery-card"));
        var empty = cut.Find("#gallery-empty").TextContent;
        Assert.Contains("No images yet", empty, StringComparison.Ordinal);
        Assert.Contains("Artifacts", empty, StringComparison.Ordinal);
        Assert.Equal("/records", cut.Find("#gallery-empty a").GetAttribute("href"));
    }

    [Fact]
    public void ALoadFailure_ShowsAnAlert()
    {
        _api.On(HttpMethod.Get, Default, _ => new HttpResponseMessage(HttpStatusCode.BadGateway));

        var cut = Render<GalleryIndex>();

        Assert.Contains("could not be loaded", cut.Find("[role=alert]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Citizens_GetNoScopeSwitch()
    {
        SetOfficer(false);
        _api.OnJson(HttpMethod.Get, Default, Page(1, Group()));

        var cut = Render<GalleryIndex>();

        Assert.Empty(cut.FindAll("#scope-mine"));
        Assert.Empty(cut.FindAll("#scope-all"));
    }

    [Fact]
    public void Officers_CanSwitchBetweenTheirOwnAndEveryonesGalleries()
    {
        SetOfficer(true);
        _api.OnJson(HttpMethod.Get, Default, Page(1, Group("Mine")));
        _api.OnJson(HttpMethod.Get, $"{Default}&scope=all", Page(2, Group("Everyone A"), Group("Everyone B")));
        var cut = Render<GalleryIndex>();
        Assert.Equal("true", cut.Find("#scope-mine").GetAttribute("aria-pressed"));

        cut.Find("#scope-all").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(2, cut.FindAll(".gallery-card").Count);
            Assert.Equal("true", cut.Find("#scope-all").GetAttribute("aria-pressed"));
            Assert.Equal("false", cut.Find("#scope-mine").GetAttribute("aria-pressed"));
        });
    }

    [Fact]
    public void Officers_StillSeeTheSwitch_WhenTheirOwnGalleryIsEmpty()
    {
        SetOfficer(true);
        _api.OnJson(HttpMethod.Get, Default, Page(0));

        var cut = Render<GalleryIndex>();

        Assert.NotNull(cut.Find("#scope-all"));
        Assert.NotNull(cut.Find("#gallery-empty"));
    }

    [Fact]
    public void Paging_ShowsThePosition_AndLoadsTheNextPage()
    {
        _api.OnJson(HttpMethod.Get, Default, Page(25, Group("Page one")));
        _api.OnJson(HttpMethod.Get, "api/gallery?page=2&pageSize=12", new PagedResult<GalleryGroupDto>(2, 12, 25, [Group("Page two")]));
        var cut = Render<GalleryIndex>();
        Assert.Contains("Page 1 of 3", cut.Find("nav[aria-label='Gallery pages']").TextContent, StringComparison.Ordinal);
        Assert.True(cut.Find("#previous-page").HasAttribute("disabled"));

        cut.Find("#next-page").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Page two", cut.Find(".gallery-card").TextContent, StringComparison.Ordinal);
            Assert.Contains("Page 2 of 3", cut.Find("nav[aria-label='Gallery pages']").TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ASinglePage_HasNoPager()
    {
        _api.OnJson(HttpMethod.Get, Default, Page(1, Group()));

        var cut = Render<GalleryIndex>();

        Assert.Empty(cut.FindAll("nav[aria-label='Gallery pages']"));
    }
}
