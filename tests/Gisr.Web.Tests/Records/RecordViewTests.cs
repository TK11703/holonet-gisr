using System.Net;
using Bunit;
using Gisr.Shared.Artifacts;
using Gisr.Shared.Common;
using Gisr.Shared.Records;
using Gisr.Web.Records;
using Gisr.Web.Tests.Common;

namespace Gisr.Web.Tests.Records;

public class RecordViewTests : BunitContext
{
    private readonly FakeApi _api = new();

    public RecordViewTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _api.OnJson(HttpMethod.Get, "api/me/affiliations", Samples.Affiliations);
        _api.AddTo(Services);
    }

    private IRenderedComponent<RecordView> RenderView(RecordDto record, params RevisionDto[] history) =>
        RenderView(record, [], history);

    private IRenderedComponent<RecordView> RenderView(RecordDto record, ArtifactDto[] artifacts, params RevisionDto[] history)
    {
        _api.OnJson(HttpMethod.Get, $"api/records/{record.Id}", record);
        _api.OnJson(HttpMethod.Get, $"api/records/{record.Id}/history?page=1&pageSize=20", new PagedResult<RevisionDto>(1, 20, history.Length, history));
        _api.OnJson(HttpMethod.Get, $"api/records/{record.Id}/artifacts?pageSize=100", new PagedResult<ArtifactDto>(1, 100, artifacts.Length, artifacts));
        return Render<RecordView>(p => p.Add(c => c.Id, record.Id));
    }

    [Fact]
    public void ArtifactsSection_ListsTheArtifacts_ReadOnly_WithoutAViewToken()
    {
        var record = Samples.Record(canEdit: true);
        var image = new ArtifactDto(Guid.NewGuid(), record.Id, "map.png", "image/png", 4096, true, "A star map", "Rex", DateTimeOffset.UtcNow, "skipped", "unsupported_type");

        var cut = RenderView(record, [image]);

        var section = cut.Find("section#artifacts");
        Assert.Equal("Artifacts", section.QuerySelector("h2")!.TextContent.Trim());
        Assert.Equal("A star map", section.QuerySelector("img")!.GetAttribute("alt"));
        Assert.Empty(cut.FindAll("button.artifact-remove"));
        Assert.Empty(cut.FindAll("#artifact-uploader"));
        Assert.DoesNotContain(_api.Calls.Where(c => c.PathAndQuery.Contains("/artifacts", StringComparison.Ordinal)), c => c.Headers.ContainsKey("X-View-Token"));
    }

    [Fact]
    public void Header_ShowsTitleAffiliationSubjectAuthorAndVersion()
    {
        var record = Samples.Record(title: "Temple guard", affiliation: "Military", version: 4);

        var cut = RenderView(record);

        Assert.Equal("Temple guard", cut.Find("h1").TextContent.Trim());
        var header = cut.Find("#record-header").TextContent;
        Assert.Contains("Military", header, StringComparison.Ordinal);
        Assert.Contains("Rex", header, StringComparison.Ordinal);
        Assert.Contains("Version 4", header, StringComparison.Ordinal);
        Assert.Contains("2026-10-02", header, StringComparison.Ordinal);
    }

    [Fact]
    public void Header_ShowsTheAuthor_AndNoServiceRecordBadge()
    {
        var record = Samples.Record() with { AuthorDisplayName = "Officer Cody", CanEdit = false };

        var cut = RenderView(record);

        var header = cut.Find("#record-header").TextContent;
        Assert.DoesNotContain("Service record", header, StringComparison.Ordinal);
        Assert.Contains("Officer Cody", header, StringComparison.Ordinal);
    }

    [Fact]
    public void HasTheSectionsInOrder()
    {
        var cut = RenderView(Samples.Record());

        var headings = cut.FindAll("h2").Select(h => h.TextContent.Trim()).ToArray();

        Assert.Equal(["Engagement details", "Incidents", "Awards", "Artifacts", "History"], headings);
    }

    [Fact]
    public void EngagementDetails_ShowTheDatesAndTheSummary()
    {
        var cut = RenderView(Samples.Record() with
        {
            EngagementDetail = new Dictionary<string, string> { ["summary"] = "Served with distinction.", ["rank"] = "Captain" },
        });

        var details = cut.Find("#engagement-details").TextContent;
        Assert.Contains("2019-01-01", details, StringComparison.Ordinal);
        Assert.Contains("2020-01-01", details, StringComparison.Ordinal);
        Assert.Contains("Summary", details, StringComparison.Ordinal);
        Assert.Contains("Served with distinction.", details, StringComparison.Ordinal);
        Assert.DoesNotContain("Captain", details, StringComparison.Ordinal);
    }

    [Fact]
    public void IncidentsAndAwards_AreListed()
    {
        var cut = RenderView(Samples.Record());

        var incidents = cut.Find("#incidents").TextContent;
        Assert.Contains("Geonosis", incidents, StringComparison.Ordinal);
        Assert.Contains("2020-05-04", incidents, StringComparison.Ordinal);
        Assert.Contains("First battle", incidents, StringComparison.Ordinal);
        var awards = cut.Find("#awards").TextContent;
        Assert.Contains("Medal of Honor", awards, StringComparison.Ordinal);
        Assert.Contains("Republic", awards, StringComparison.Ordinal);
        Assert.Contains("For valor", awards, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyCollections_ShowNeutralMessages()
    {
        var cut = RenderView(Samples.Record() with { Incidents = [], Awards = [] });

        Assert.Contains("No incidents recorded", cut.Find("#incidents").TextContent, StringComparison.Ordinal);
        Assert.Contains("No awards recorded", cut.Find("#awards").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void History_ListsRevisionsNewestFirst()
    {
        var changedAt = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

        var cut = RenderView(
            Samples.Record(),
            new RevisionDto(2, "Rex", changedAt, "Military", "Renamed"),
            new RevisionDto(1, "Rex", changedAt.AddDays(-1), "Military", "Original"));

        var rows = cut.FindAll("#history tbody tr").Select(r => r.TextContent).ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Contains("Renamed", rows[0], StringComparison.Ordinal);
        Assert.Contains("Original", rows[1], StringComparison.Ordinal);
        Assert.Contains("2026-10-02", rows[0], StringComparison.Ordinal);
    }

    [Fact]
    public void EditButton_IsShownOnlyWhenTheRecordCanBeEdited()
    {
        var editable = Samples.Record(canEdit: true);
        var cut = RenderView(editable);
        Assert.Equal($"/records/{editable.Id}/edit", cut.Find("a#edit-record").GetAttribute("href"));

        var readOnly = Samples.Record(canEdit: false);
        var other = RenderView(readOnly);
        Assert.Empty(other.FindAll("a#edit-record"));
    }

    [Fact]
    public void UnknownRecord_ShowsNotFound()
    {
        var id = Guid.NewGuid();
        _api.On(HttpMethod.Get, $"api/records/{id}", _ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var cut = Render<RecordView>(p => p.Add(c => c.Id, id));

        Assert.Contains("not found", cut.Find("[role=alert]").TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(cut.FindAll("#engagement-details"));
    }

    [Fact]
    public void LoadFailure_ShowsAnError()
    {
        var id = Guid.NewGuid();
        _api.On(HttpMethod.Get, $"api/records/{id}", _ => new HttpResponseMessage(HttpStatusCode.BadGateway));

        var cut = Render<RecordView>(p => p.Add(c => c.Id, id));

        Assert.Contains("could not be loaded", cut.Find("[role=alert]").TextContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UserText_IsRenderedAsText_NotMarkup()
    {
        var record = Samples.Record(title: "<script>alert(1)</script>");

        var cut = RenderView(record);

        Assert.Empty(cut.FindAll("script"));
        Assert.Contains("<script>", cut.Find("h1").TextContent, StringComparison.Ordinal);
    }
}
