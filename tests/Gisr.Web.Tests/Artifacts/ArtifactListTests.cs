using System.Net;
using System.Text.Json;
using Bunit;
using Gisr.Shared.Artifacts;
using Gisr.Shared.Common;
using Gisr.Web.Artifacts;
using Gisr.Web.Tests.Common;
using Microsoft.AspNetCore.Components;

namespace Gisr.Web.Tests.Artifacts;

public class ArtifactListTests : BunitContext
{
    private const string ViewToken = "view-token-view-token-view-token-view-token-1234";

    private readonly FakeApi _api = new();
    private readonly Guid _recordId = Guid.NewGuid();

    public ArtifactListTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _api.AddTo(Services);
    }

    private string ListPath => $"api/records/{_recordId}/artifacts?pageSize=100";

    private static ArtifactDto Doc(string name = "orders.pdf", string status = "pending", string? detail = null, long size = 2048) =>
        new(Guid.NewGuid(), Guid.Empty, name, "application/pdf", size, false, null, "Obi-Wan", new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero), status, detail);

    private static ArtifactDto Image(string name = "map.png", string description = "A star map") =>
        new(Guid.NewGuid(), Guid.Empty, name, "image/png", 4096, true, description, "Obi-Wan", new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero), "skipped", "unsupported_type");

    private void Serve(params ArtifactDto[] items) =>
        _api.OnJson(HttpMethod.Get, ListPath, new PagedResult<ArtifactDto>(1, 100, items.Length, items));

    private IRenderedComponent<ArtifactList> RenderList(
        bool editable = false,
        Guid? viewingId = null,
        string? token = null,
        Action? onAccessLost = null,
        Action? onChanged = null,
        int refresh = 0) =>
        Render<ArtifactList>(p =>
        {
            p.Add(c => c.RecordId, _recordId);
            p.Add(c => c.Editable, editable);
            p.Add(c => c.RefreshKey, refresh);
            if (viewingId is not null)
            {
                p.Add(c => c.ViewingId, viewingId);
                p.Add(c => c.ViewToken, token);
            }

            if (onAccessLost is not null)
            {
                p.Add(c => c.OnAccessLost, EventCallback.Factory.Create(this, onAccessLost));
            }

            if (onChanged is not null)
            {
                p.Add(c => c.OnChanged, EventCallback.Factory.Create(this, onChanged));
            }
        });

    [Fact]
    public void Images_ShowAThumbnail_WithTheDescriptionAsAltText_LinkingToTheFullImage()
    {
        var image = Image();
        Serve(image);

        var cut = RenderList();

        var thumbnail = cut.Find("li.artifact-item img");
        Assert.Equal($"/media/artifacts/{image.Id}/thumbnail", thumbnail.GetAttribute("src"));
        Assert.Equal("A star map", thumbnail.GetAttribute("alt"));
        Assert.Equal($"/media/artifacts/{image.Id}", cut.Find("li.artifact-item a.artifact-open").GetAttribute("href"));
        Assert.DoesNotContain("A star map", cut.Find("li.artifact-item").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Files_ShowAnIconAndADownloadLink_WithSizeAndUploader()
    {
        var doc = Doc("orders.pdf", size: 2048);
        Serve(doc);

        var cut = RenderList();

        var link = cut.Find("li.artifact-item a.artifact-download");
        Assert.Equal($"/media/artifacts/{doc.Id}", link.GetAttribute("href"));
        Assert.True(link.HasAttribute("download"));
        Assert.Equal("orders.pdf", link.TextContent.Trim());
        Assert.Empty(cut.FindAll("li.artifact-item img"));
        var text = cut.Find("li.artifact-item").TextContent;
        Assert.Contains("2.0 KB", text, StringComparison.Ordinal);
        Assert.Contains("Obi-Wan", link.GetAttribute("title"), StringComparison.Ordinal);
        Assert.Contains("2026-10-02", link.GetAttribute("title"), StringComparison.Ordinal);
    }

    [Fact]
    public void AnImage_AlsoOffersADownload()
    {
        var image = Image("map.png");
        Serve(image);

        var cut = RenderList();

        Assert.Equal("map.png", cut.Find("li.artifact-item a.artifact-download").TextContent.Trim());
    }

    [Fact]
    public void FilesThatAreNotIndexed_ShowANotSearchableBadge_WithTheReason()
    {
        Serve(Image(), Doc("scan.pdf", "skipped", "no_text"), Doc("report.pdf", "failed", "parse_error"));

        var cut = RenderList();

        var items = cut.FindAll("li.artifact-item");
        Assert.Equal("Not searchable", items[0].QuerySelector(".artifact-search-badge")!.TextContent.Trim());
        Assert.Contains("Images are not searched by content", items[0].QuerySelector(".artifact-search-reason")!.TextContent, StringComparison.Ordinal);
        Assert.Equal("Not searchable", items[1].QuerySelector(".artifact-search-badge")!.TextContent.Trim());
        Assert.Contains("No readable text", items[1].QuerySelector(".artifact-search-reason")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("could not be read", items[2].QuerySelector(".artifact-search-reason")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void PendingFilesAreMarkedAsIndexing_AndIndexedFilesCarryNoBadge()
    {
        Serve(Doc("new.pdf", "pending"), Doc("done.pdf", "indexed"));

        var cut = RenderList();

        var items = cut.FindAll("li.artifact-item");
        Assert.Equal("Indexing pending", items[0].QuerySelector(".artifact-search-badge")!.TextContent.Trim());
        Assert.Empty(items[1].QuerySelectorAll(".artifact-search-badge"));
    }

    [Fact]
    public void APartiallyIndexedFile_ExplainsThePageLimit()
    {
        Serve(Doc("big-scan.pdf", "indexed", "ocr_page_limit"));

        var cut = RenderList();

        Assert.Contains("first 100 pages", cut.Find("li.artifact-item .artifact-search-reason").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyList_SaysSo()
    {
        Serve();

        var cut = RenderList();

        Assert.Contains("No artifacts attached", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ALoadFailure_ShowsAnAlert()
    {
        _api.On(HttpMethod.Get, ListPath, _ => new HttpResponseMessage(HttpStatusCode.BadGateway));

        var cut = RenderList();

        Assert.Contains("could not be loaded", cut.Find("[role=alert]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void ANotFoundOutsideAViewing_IsJustALoadFailure_AndNotAccessLoss()
    {
        var lost = 0;
        _api.On(HttpMethod.Get, ListPath, _ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var cut = RenderList(onAccessLost: () => lost++);

        Assert.Equal(0, lost);
        Assert.Contains("could not be loaded", cut.Find("[role=alert]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadOnlyLists_OfferNoChanges()
    {
        Serve(Image(), Doc());

        var cut = RenderList(editable: false);

        Assert.Empty(cut.FindAll("button.artifact-remove"));
    }

    [Fact]
    public void Removing_AsksForConfirmation_First()
    {
        var doc = Doc();
        Serve(doc);
        _api.On(HttpMethod.Delete, $"api/artifacts/{doc.Id}", _ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var cut = RenderList(editable: true);

        cut.Find($"#remove-{doc.Id}").Click();

        Assert.Contains("Remove orders.pdf?", cut.Find("li.artifact-item .artifact-confirm").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain(_api.Calls, c => c.Method == HttpMethod.Delete);
        cut.Find($"#cancel-remove-{doc.Id}").Click();
        Assert.Empty(cut.FindAll(".artifact-confirm"));
        Assert.DoesNotContain(_api.Calls, c => c.Method == HttpMethod.Delete);
    }

    [Fact]
    public void ConfirmingARemoval_DeletesTheArtifact_AndReloadsTheList()
    {
        var doc = Doc();
        var other = Doc("keep.pdf");
        var changed = 0;
        var removed = false;
        _api.On(HttpMethod.Get, ListPath, _ => FakeApi.JsonResponse(HttpStatusCode.OK,
            removed ? new PagedResult<ArtifactDto>(1, 100, 1, [other]) : new PagedResult<ArtifactDto>(1, 100, 2, [doc, other])));
        _api.On(HttpMethod.Delete, $"api/artifacts/{doc.Id}", _ =>
        {
            removed = true;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var cut = RenderList(editable: true, onChanged: () => changed++);

        cut.Find($"#remove-{doc.Id}").Click();
        cut.Find($"#confirm-remove-{doc.Id}").Click();

        cut.WaitForAssertion(() =>
        {
            var item = Assert.Single(cut.FindAll("li.artifact-item"));
            Assert.Contains("keep.pdf", item.TextContent, StringComparison.Ordinal);
        });
        Assert.Equal(1, changed);
    }

    [Fact]
    public void AFailedRemoval_ShowsAnError_AndKeepsTheArtifact()
    {
        var doc = Doc();
        Serve(doc);
        _api.On(HttpMethod.Delete, $"api/artifacts/{doc.Id}", _ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var cut = RenderList(editable: true);

        cut.Find($"#remove-{doc.Id}").Click();
        cut.Find($"#confirm-remove-{doc.Id}").Click();

        cut.WaitForAssertion(() => Assert.Contains("could not be removed", cut.Find("[role=alert]").TextContent, StringComparison.Ordinal));
        Assert.Single(cut.FindAll("li.artifact-item"));
    }

    [Fact]
    public void TheRemoveButtons_HaveAccessibleNames()
    {
        var doc = Doc("orders.pdf");
        Serve(doc);

        var cut = RenderList(editable: true);

        Assert.Equal("Remove orders.pdf", cut.Find($"#remove-{doc.Id}").GetAttribute("aria-label"));
    }

    [Fact]
    public void TheArtifacts_HaveNoDescriptionEditing()
    {
        Serve(Image());

        var cut = RenderList(editable: true);

        Assert.Empty(cut.FindAll("[id^=edit-description-], [id^=save-description-]"));
    }

    [Fact]
    public void DuringAViewing_UrlsCarryTheViewingId_AndTheTokenGoesOnlyInTheApiHeader()
    {
        var viewingId = Guid.NewGuid();
        var image = Image();
        var doc = Doc();
        Serve(image, doc);

        var cut = RenderList(viewingId: viewingId, token: ViewToken);

        Assert.Equal($"/media/artifacts/{image.Id}/thumbnail?v={viewingId}", cut.Find("img").GetAttribute("src"));
        Assert.Equal($"/media/artifacts/{image.Id}?v={viewingId}", cut.Find("a.artifact-open").GetAttribute("href"));
        Assert.Equal($"/media/artifacts/{doc.Id}?v={viewingId}", cut.FindAll("a.artifact-download")[1].GetAttribute("href"));
        Assert.DoesNotContain(ViewToken, cut.Markup, StringComparison.Ordinal);
        Assert.Equal(ViewToken, _api.Calls.Single(c => c.PathAndQuery == ListPath).Headers["X-View-Token"]);
    }

    [Fact]
    public void DuringAViewing_ANotFound_ReportsLostAccess_AndShowsNoArtifacts()
    {
        var lost = 0;
        _api.On(HttpMethod.Get, ListPath, _ => FakeApi.Problem(HttpStatusCode.NotFound, new { status = 404 }));

        var cut = RenderList(viewingId: Guid.NewGuid(), token: ViewToken, onAccessLost: () => lost++);

        Assert.Equal(1, lost);
        Assert.Empty(cut.FindAll("li.artifact-item"));
    }

    [Fact]
    public void ViewingListsAreReadOnly_EvenWhenEditableWasRequested()
    {
        Serve(Doc());

        var cut = RenderList(editable: true, viewingId: Guid.NewGuid(), token: ViewToken);

        Assert.Empty(cut.FindAll("button.artifact-remove"));
    }

    [Fact]
    public void ChangingTheRefreshKey_ReloadsTheList()
    {
        Serve(Doc("first.pdf"));
        var cut = RenderList(refresh: 0);
        Assert.Contains("first.pdf", cut.Markup, StringComparison.Ordinal);
        Serve(Doc("first.pdf"), Doc("second.pdf"));

        cut.Render(p => p.Add(c => c.RefreshKey, 1));

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("li.artifact-item").Count));
    }
}
