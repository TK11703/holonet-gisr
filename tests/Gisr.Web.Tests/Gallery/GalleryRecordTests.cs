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
    public void ShowsTheRecordTitle_WithABreadcrumbBackToTheGallery_AndALinkToTheRecord()
    {
        ServeImages(1, 0);

        var cut = RenderPage();

        Assert.Equal("Temple guard", cut.Find("h1").TextContent.Trim());
        var crumbs = cut.FindAll("nav[aria-label=breadcrumb] a");
        Assert.Equal("/gallery", crumbs[^1].GetAttribute("href"));
        Assert.Equal($"/records/{_record.Id}", cut.Find("a#open-record").GetAttribute("href"));
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
    public void TheLightbox_NavigatesWithinThePage_AndLinksToTheRecord()
    {
        var a = Image("a.png", "First");
        var b = Image("b.png", "Second");
        ServeImages(1, 2, a, b);
        var cut = RenderPage();
        cut.FindAll("button.gallery-thumb")[0].Click();

        cut.Find("#lightbox-next").Click();

        Assert.Equal($"/media/artifacts/{b.Id}", cut.Find("img.lightbox-image").GetAttribute("src"));
        Assert.Equal($"/records/{_record.Id}", cut.Find("a.lightbox-open-record").GetAttribute("href"));
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
