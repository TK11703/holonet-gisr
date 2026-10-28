using System.Net;
using Bunit;
using Gisr.Shared.Artifacts;
using Gisr.Shared.Common;
using Gisr.Shared.Records;
using Gisr.Web.Gallery;
using Gisr.Web.Layout;
using Gisr.Web.Tests.Common;
using Gisr.Web.Tests.Records;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Gisr.Web.Tests.Gallery;

public class GalleryRecordTests : BunitContext
{
    private readonly FakeApi _api = new();
    private readonly RecordDto _record = Samples.Record(title: "Temple guard");

    public GalleryRecordTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _api.AddTo(Services);
        Services.AddScoped<ShellInterop>();
        _api.OnJson(HttpMethod.Get, $"api/records/{_record.Id}", _record);
    }

    private string CurrentUri => Services.GetRequiredService<NavigationManager>().Uri;

    private string ImagesPath(int page = 1) => $"api/records/{_record.Id}/artifacts?kind=image&page={page}&pageSize=24";

    private ArtifactDto Image(string name, string description) =>
        new(Guid.NewGuid(), _record.Id, name, "image/png", 100, true, description, "Rex", DateTimeOffset.UtcNow, "skipped", "unsupported_type");

    private void ServeImages(int page, long total, params ArtifactDto[] items) =>
        _api.OnJson(HttpMethod.Get, ImagesPath(page), new PagedResult<ArtifactDto>(page, 24, total, items));

    private IRenderedComponent<GalleryRecord> RenderPage() => Render<GalleryRecord>(p => p.Add(c => c.RecordId, _record.Id));

    [Fact]
    public void ShowsGalleryDetailsAsThePageTitle_WithABreadcrumbBackToTheGallery_AndALinkToTheRecord()
    {
        ServeImages(1, 0);

        var cut = RenderPage();

        Assert.Equal("Gallery details", cut.Find("h1").TextContent.Trim());
        var crumbs = cut.FindAll("nav[aria-label=breadcrumb] a");
        Assert.Equal("/gallery", crumbs[^1].GetAttribute("href"));
        Assert.Equal($"/records/{_record.Id}", cut.Find("a#open-record").GetAttribute("href"));
    }

    [Fact]
    public void ShowsARecordSummary_WithTitleSubjectAndAffiliation_AboveTheImages()
    {
        ServeImages(1, 1, Image("a.png", "A temple at dawn"));

        var cut = RenderPage();

        var summary = cut.Find("#gallery-record-summary");
        Assert.Equal("Record details", summary.QuerySelector("legend")!.TextContent.Trim());
        Assert.Equal("Temple guard", summary.QuerySelector("#gallery-record-title")!.TextContent.Trim());
        Assert.Equal(["Title", "Subject", "Affiliation"], summary.QuerySelectorAll("dt").Select(t => t.TextContent.Trim()));
        Assert.Equal($"/records/{_record.Id}", summary.QuerySelector("a#open-record")!.GetAttribute("href"));
        Assert.Contains("col-md-1", summary.QuerySelector("a#open-record")!.ParentElement!.ClassList);
        Assert.Contains(_record.SubjectDisplayName, summary.TextContent, StringComparison.Ordinal);
        Assert.Contains(_record.Affiliation, summary.TextContent, StringComparison.Ordinal);
        Assert.True(cut.Markup.IndexOf("gallery-record-summary", StringComparison.Ordinal) < cut.Markup.IndexOf("artifact-grid", StringComparison.Ordinal));
    }

    [Fact]
    public void TheSummaryBarShowsTheItemCountAndTotalSize_AboveTilesWithNameAndSize()
    {
        var a = Image("a.png", "A") with { SizeBytes = 2048 };
        var b = Image("b.png", "B") with { SizeBytes = 1024 * 1024 };
        ServeImages(1, 2, a, b);

        var cut = RenderPage();

        Assert.Empty(cut.FindAll("#gallery-images .card-header"));
        var bar = cut.Find("#gallery-images-summary");
        Assert.Contains("Items in this gallery", bar.TextContent, StringComparison.Ordinal);
        Assert.Equal("2", cut.Find("#gallery-item-count").TextContent.Trim());
        var total = System.Text.RegularExpressions.Regex.Replace(cut.Find("#gallery-total-size").TextContent, @"\s+", " ").Trim();
        Assert.Equal("Total memory used 1.0 MB", total);
        var tiles = cut.FindAll("#gallery-images .artifact-tile");
        Assert.Equal(2, tiles.Count);
        Assert.Contains("a.png", tiles[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("2.0 KB", tiles[0].TextContent, StringComparison.Ordinal);
        Assert.Equal(2, cut.FindAll("button.gallery-thumb").Count);
    }

    [Fact]
    public void OpenRecord_IsAnIconOnlyLinkWithATitle()
    {
        ServeImages(1, 0);

        var link = RenderPage().Find("a#open-record");

        Assert.Equal("Open record", link.GetAttribute("title"));
        Assert.Equal("Open record", link.GetAttribute("aria-label"));
        Assert.Equal(string.Empty, link.TextContent.Trim());
    }

    [Fact]
    public void TotalSize_CoversEveryPage()
    {
        ServeImages(1, 30, Image("one.png", "First") with { SizeBytes = 1024 });
        _api.OnJson(HttpMethod.Get, $"api/records/{_record.Id}/artifacts?kind=image&pageSize=100",
            new PagedResult<ArtifactDto>(1, 100, 30, [Image("one.png", "First") with { SizeBytes = 1024 * 1024 }, Image("two.png", "Second") with { SizeBytes = 1024 * 1024 }]));

        var cut = RenderPage();

        Assert.Contains("2.0 MB", cut.Find("#gallery-total-size").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowsAThumbnailForEveryImage_WithTheDescriptionAsAltText()
    {
        var a = Image("a.png", "A temple at dawn");
        var b = Image("b.png", "The council chamber");
        ServeImages(1, 2, a, b);

        var cut = RenderPage();

        var thumbnails = cut.FindAll("button.gallery-thumb img");
        Assert.Equal([$"/media/artifacts/{a.Id}/thumbnail", $"/media/artifacts/{b.Id}/thumbnail"], thumbnails.Select(t => t.GetAttribute("src")));
        Assert.Equal(["A temple at dawn", "The council chamber"], thumbnails.Select(t => t.GetAttribute("alt")));
        Assert.Equal("Open A temple at dawn", cut.FindAll("button.gallery-thumb")[0].GetAttribute("aria-label"));
    }

    [Fact]
    public void ARecordWithoutImages_SaysSo()
    {
        ServeImages(1, 0);

        var cut = RenderPage();

        Assert.Contains("no images", cut.Find("#gallery-record-empty").TextContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Paging_LoadsTheNextPageOfThumbnails()
    {
        ServeImages(1, 30, Image("one.png", "First page"));
        ServeImages(2, 30, Image("two.png", "Second page"));
        var cut = RenderPage();
        Assert.Contains("Page 1 of 2", cut.Find("nav[aria-label='Gallery pages']").TextContent, StringComparison.Ordinal);

        cut.Find("#next-page").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Second page", cut.Find("button.gallery-thumb img").GetAttribute("alt"));
            Assert.Contains("Page 2 of 2", cut.Find("nav[aria-label='Gallery pages']").TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void SelectingAThumbnail_OpensTheLightboxOnThatImage_AndClosingRemovesIt()
    {
        var a = Image("a.png", "First");
        var b = Image("b.png", "Second");
        ServeImages(1, 2, a, b);
        var cut = RenderPage();
        Assert.Empty(cut.FindAll("[role=dialog]"));

        cut.FindAll("button.gallery-thumb")[1].Click();

        Assert.Equal($"/media/artifacts/{b.Id}", cut.Find("img.lightbox-image").GetAttribute("src"));
        cut.Find("#lightbox-close").Click();
        Assert.Empty(cut.FindAll("[role=dialog]"));
    }

    [Fact]
    public void TheLightbox_NavigatesWithinThePage()
    {
        var a = Image("a.png", "First");
        var b = Image("b.png", "Second");
        ServeImages(1, 2, a, b);
        var cut = RenderPage();
        cut.FindAll("button.gallery-thumb")[0].Click();

        cut.Find("#lightbox-next").Click();

        Assert.Equal($"/media/artifacts/{b.Id}", cut.Find("img.lightbox-image").GetAttribute("src"));
    }

    [Fact]
    public void ASealedRecord_RedirectsToItsRecordPage_ForItsSubject()
    {
        _api.On(HttpMethod.Get, $"api/records/{_record.Id}", _ => FakeApi.Problem(HttpStatusCode.Forbidden, new Dictionary<string, object>
        {
            ["title"] = "Forbidden",
            ["status"] = 403,
            ["sealed"] = true,
        }));

        RenderPage();

        Assert.EndsWith($"/records/{_record.Id}", CurrentUri, StringComparison.Ordinal);
        Assert.DoesNotContain(_api.Calls, c => c.PathAndQuery.Contains("/artifacts", StringComparison.Ordinal));
    }

    [Fact]
    public void ASealedRecord_RedirectsToItsRecordPage_ForAnOfficerToo()
    {
        _api.OnJson(HttpMethod.Get, $"api/records/{_record.Id}", _record with { IsSealed = true });

        RenderPage();

        Assert.EndsWith($"/records/{_record.Id}", CurrentUri, StringComparison.Ordinal);
        Assert.DoesNotContain(_api.Calls, c => c.PathAndQuery.Contains("/artifacts", StringComparison.Ordinal));
    }

    [Fact]
    public void AnUnknownRecord_ShowsNotFound()
    {
        _api.On(HttpMethod.Get, $"api/records/{_record.Id}", _ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var cut = RenderPage();

        Assert.Contains("not found", cut.Find("[role=alert]").TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(cut.FindAll("button.gallery-thumb"));
    }

    [Fact]
    public void ALoadFailure_ShowsAnAlert()
    {
        _api.On(HttpMethod.Get, ImagesPath(), _ => new HttpResponseMessage(HttpStatusCode.BadGateway));

        var cut = RenderPage();

        Assert.Contains("could not be loaded", cut.Find("[role=alert]").TextContent, StringComparison.Ordinal);
    }
}
