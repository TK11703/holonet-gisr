using System.Net;
using Bunit;
using Gisr.Shared.Common;
using Gisr.Shared.Records;
using Gisr.Web.Records;
using Gisr.Web.Tests.Common;

namespace Gisr.Web.Tests.Records;

public class RecordListTests : BunitContext
{
    private const string Default = "api/records?page=1&pageSize=20&sort=updatedDesc";

    private readonly FakeApi _api = new();

    public RecordListTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _api.OnJson(HttpMethod.Get, "api/affiliations", Samples.Affiliations);
        _api.AddTo(Services);
    }

    private static PagedResult<RecordSummaryDto> Page(int total, int page = 1, params RecordSummaryDto[] items) =>
        new(page, 20, total, items);

    [Fact]
    public void RendersTitleBreadcrumbAndColumns()
    {
        _api.OnJson(HttpMethod.Get, Default, Page(1, 1, Samples.ListItem()));

        var cut = Render<RecordList>();

        Assert.Equal("Citizen records", cut.Find("h1").TextContent.Trim());
        Assert.NotNull(cut.Find("nav[aria-label=breadcrumb]"));
        var headers = cut.FindAll("table thead th").Select(h => h.TextContent.Trim()).ToArray();
        Assert.Equal(["Title", "Affiliation", "Subject", "Engagement", "Artifacts", "Updated"], headers);
    }

    [Fact]
    public void Row_ShowsTheRecordWithALinkToItsPage()
    {
        var item = Samples.ListItem(title: "Temple guard", affiliation: "jedi");
        _api.OnJson(HttpMethod.Get, Default, Page(1, 1, item));

        var cut = Render<RecordList>();

        var link = cut.Find($"table a[href='/records/{item.Id}']");
        Assert.Equal("Temple guard", link.TextContent.Trim());
        var row = cut.Find("tbody tr").TextContent;
        Assert.Contains("Jedi", row, StringComparison.Ordinal);
        Assert.Contains("Rex", row, StringComparison.Ordinal);
        Assert.Contains("2019-01-01", row, StringComparison.Ordinal);
        Assert.Contains("2026-10-02", row, StringComparison.Ordinal);
        Assert.DoesNotContain("Service record", row, StringComparison.Ordinal);
    }

    [Fact]
    public void Row_ShowsHowManyArtifactsTheRecordHas()
    {
        _api.OnJson(HttpMethod.Get, Default, Page(2, 1, Samples.ListItem(title: "With files") with { ArtifactCount = 3 }, Samples.ListItem(title: "No files") with { ArtifactCount = 0 }));

        var cut = Render<RecordList>();

        var rows = cut.FindAll("tbody tr");
        Assert.Equal("3", rows[0].QuerySelectorAll("td")[4].TextContent.Trim());
        Assert.Equal("0", rows[1].QuerySelectorAll("td")[4].TextContent.Trim());
    }

    [Fact]
    public void ServiceRecords_AreBadged()
    {
        _api.OnJson(HttpMethod.Get, Default, Page(1, 1, Samples.ListItem(service: true)));

        var cut = Render<RecordList>();

        Assert.Contains("Service record", cut.Find("tbody tr").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyList_InvitesTheUserToCreateTheirFirstRecord()
    {
        _api.OnJson(HttpMethod.Get, Default, Page(0));

        var cut = Render<RecordList>();

        Assert.Contains("no citizen records", cut.Markup, StringComparison.OrdinalIgnoreCase);
        var link = cut.Find("a[href='/records/new']");
        Assert.Contains("Create your first record", link.TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("table"));
    }

    [Fact]
    public void AffiliationFilter_ListsAffiliations_AndReloadsFromPageOne()
    {
        _api.OnJson(HttpMethod.Get, Default, Page(1, 1, Samples.ListItem()));
        _api.OnJson(HttpMethod.Get, $"{Default}&affiliation=jedi", Page(1, 1, Samples.ListItem(title: "Knight", affiliation: "jedi")));
        var cut = Render<RecordList>();

        Assert.Equal(["All affiliations", "Military", "Jedi"],
            cut.FindAll("select#filter-affiliation option").Select(o => o.TextContent.Trim()).ToArray());
        cut.Find("select#filter-affiliation").Change("jedi");

        cut.WaitForAssertion(() => Assert.Contains("Knight", cut.Find("tbody").TextContent, StringComparison.Ordinal));
        Assert.Contains(_api.Calls, c => c.PathAndQuery == $"{Default}&affiliation=jedi");
    }

    [Fact]
    public void FilterWithNoMatches_ShowsNoMatchMessage_NotTheFirstRecordInvitation()
    {
        _api.OnJson(HttpMethod.Get, Default, Page(1, 1, Samples.ListItem()));
        _api.OnJson(HttpMethod.Get, $"{Default}&affiliation=jedi", Page(0));
        var cut = Render<RecordList>();

        cut.Find("select#filter-affiliation").Change("jedi");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("No records match", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Create your first record", cut.Markup, StringComparison.Ordinal);
            Assert.NotNull(cut.Find("select#filter-affiliation"));
        });
    }

    [Fact]
    public void Sort_OffersTheDocumentedOrders_AndReloads()
    {
        _api.OnJson(HttpMethod.Get, Default, Page(1, 1, Samples.ListItem()));
        _api.OnJson(HttpMethod.Get, "api/records?page=1&pageSize=20&sort=titleAsc", Page(1, 1, Samples.ListItem(title: "Alpha")));
        var cut = Render<RecordList>();

        Assert.Equal(["updatedDesc", "updatedAsc", "titleAsc", "titleDesc"],
            cut.FindAll("select#sort option").Select(o => o.GetAttribute("value")!).ToArray());
        cut.Find("select#sort").Change("titleAsc");

        cut.WaitForAssertion(() => Assert.Contains("Alpha", cut.Find("tbody").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void Paging_ShowsPosition_AndMovesBetweenPages()
    {
        _api.OnJson(HttpMethod.Get, Default, Page(45, 1, Samples.ListItem(title: "First page")));
        _api.OnJson(HttpMethod.Get, "api/records?page=2&pageSize=20&sort=updatedDesc", Page(45, 2, Samples.ListItem(title: "Second page")));
        var cut = Render<RecordList>();

        Assert.Contains("Page 1 of 3", cut.Find("nav[aria-label='Record pages']").TextContent, StringComparison.Ordinal);
        Assert.True(cut.Find("button#previous-page").HasAttribute("disabled"));
        cut.Find("button#next-page").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Second page", cut.Find("tbody").TextContent, StringComparison.Ordinal);
            Assert.Contains("Page 2 of 3", cut.Find("nav[aria-label='Record pages']").TextContent, StringComparison.Ordinal);
            Assert.False(cut.Find("button#previous-page").HasAttribute("disabled"));
        });
    }

    [Fact]
    public void SinglePage_HidesPaging()
    {
        _api.OnJson(HttpMethod.Get, Default, Page(3, 1, Samples.ListItem()));

        var cut = Render<RecordList>();

        Assert.Empty(cut.FindAll("nav[aria-label='Record pages']"));
    }

    [Fact]
    public void LoadFailure_ShowsAnError()
    {
        _api.On(HttpMethod.Get, Default, _ => new HttpResponseMessage(HttpStatusCode.BadGateway));

        var cut = Render<RecordList>();

        Assert.Contains("could not be loaded", cut.Find("[role=alert]").TextContent, StringComparison.OrdinalIgnoreCase);
    }
}
