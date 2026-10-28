using System.Net;
using Bunit;
using Gisr.Shared.Artifacts;
using Gisr.Shared.Common;
using Gisr.Shared.Records;
using Gisr.Web.Layout;
using Gisr.Web.Records;
using Gisr.Web.Tests.Common;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Gisr.Web.Tests.Records;

public class RecordViewTests : BunitContext
{
    private readonly FakeApi _api = new();

    public RecordViewTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _api.OnJson(HttpMethod.Get, "api/me/affiliations", Samples.Affiliations);
        _api.AddTo(Services);
        Services.AddScoped<ShellInterop>();
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
        Assert.Equal("Artifacts", section.QuerySelector("h2 .accordion-title")!.TextContent.Trim());
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

        Assert.Equal("Record item", cut.Find("h1").TextContent.Trim());
        Assert.Equal("Temple guard", cut.Find("nav[aria-label=breadcrumb] li.active").TextContent.Trim());
        Assert.Empty(cut.FindAll("#record-header #record-title"));
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
    public void Header_PutsSubjectAffiliationAndEngagementPeriodInThreeColumns_AndAuthorAndRevisionBelow()
    {
        var cut = RenderView(Samples.Record(affiliation: "Military"));

        var columns = cut.FindAll("#record-header dl > div").ToArray();
        Assert.Equal(5, columns.Length);
        Assert.All(columns, c => Assert.Contains("col-md-4", c.ClassList));
        Assert.Equal(["Subject", "Affiliation", "Engagement period", "Author", "Revision"],
            columns.Select(c => c.QuerySelector("dt")!.TextContent.Trim()));
        Assert.Equal("Rex", columns[0].QuerySelector("dd")!.TextContent.Trim());
        Assert.Equal("Military", columns[1].QuerySelector("dd")!.TextContent.Trim());
        Assert.Equal("2019-01-01 \u2013 2020-01-01", columns[2].QuerySelector("dd")!.TextContent.Trim());
    }

    [Fact]
    public void HasTheSectionsInOrder()
    {
        var cut = RenderView(Samples.Record());

        var headings = cut.FindAll("h2 .accordion-title").Select(h => h.TextContent.Trim()).ToArray();

        Assert.Equal(["Incidents", "Awards", "Artifacts", "History"], headings);
    }

    [Fact]
    public void Sections_AreAccordionItemsThatOpenIndependently()
    {
        var cut = RenderView(Samples.Record());

        Assert.All(["incidents", "awards", "artifacts", "history"], id =>
            Assert.Equal("false", cut.Find($"#{id}-toggle").GetAttribute("aria-expanded")));

        cut.Find("#incidents-toggle").Click();
        cut.Find("#awards-toggle").Click();

        Assert.Equal("true", cut.Find("#incidents-toggle").GetAttribute("aria-expanded"));
        Assert.Equal("true", cut.Find("#awards-toggle").GetAttribute("aria-expanded"));
        Assert.Contains("show", cut.Find("#incidents-panel").ClassList);
        Assert.Contains("show", cut.Find("#awards-panel").ClassList);
        Assert.DoesNotContain("show", cut.Find("#history-panel").ClassList);

        cut.Find("#incidents-toggle").Click();

        Assert.Equal("false", cut.Find("#incidents-toggle").GetAttribute("aria-expanded"));
        Assert.Equal("true", cut.Find("#awards-toggle").GetAttribute("aria-expanded"));
    }

    [Fact]
    public void EngagementSummary_IsShownWithoutALabel_AndPeriodStaysInTheHeader()
    {
        var cut = RenderView(Samples.Record() with
        {
            EngagementDetail = new Dictionary<string, string> { ["summary"] = "Served with distinction.", ["rank"] = "Captain" },
        });

        var summary = cut.Find("#engagement-summary");
        Assert.Equal("Summary", summary.QuerySelector("legend")!.TextContent.Trim());
        Assert.Equal("Served with distinction.", summary.QuerySelector("p")!.TextContent.Trim());
        Assert.DoesNotContain("Captain", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("2019-01-01", cut.Find("#engagement-period").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void EngagementSummary_IsOmittedWhenThereIsNone()
    {
        var cut = RenderView(Samples.Record() with { EngagementDetail = new Dictionary<string, string>() });

        Assert.Empty(cut.FindAll("#engagement-summary"));
    }

    [Fact]
    public void EngagementPeriod_WithoutDates_SaysTheyWereNotProvided()
    {
        var cut = RenderView(Samples.Record() with { EngagementStart = null, EngagementEnd = null });

        Assert.Contains("Dates were not provided.", cut.Find("#engagement-period").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void AccordionHeaders_ShowACountBadgeOnlyWhenTheSectionHasItems()
    {
        var changedAt = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
        var image = new ArtifactDto(Guid.NewGuid(), Guid.Empty, "a.png", "image/png", 10, true, "A", "Rex", changedAt, "skipped", "unsupported_type");
        var record = Samples.Record();
        var cut = RenderView(
            record with { Awards = [] },
            [image, image with { Id = Guid.NewGuid() }],
            new RevisionDto(2, "Rex", changedAt, "Military", "Renamed"),
            new RevisionDto(1, "Rex", changedAt, "Military", "Original"));

        Assert.Equal("1", cut.Find("#incidents-count").TextContent.Trim());
        Assert.Empty(cut.FindAll("#awards-count"));
        cut.WaitForAssertion(() => Assert.Equal("2", cut.Find("#artifacts-count").TextContent.Trim()));
        Assert.Equal("2", cut.Find("#history-count").TextContent.Trim());
    }

    [Fact]
    public void EmptySections_ShowAnInfoAlertWithAnIcon()
    {
        var cut = RenderView(Samples.Record() with { Incidents = [], Awards = [] });

        foreach (var id in new[] { "incidents-empty", "awards-empty" })
        {
            var notice = cut.Find($"#{id}");
            Assert.Contains("alert-info", notice.ClassList);
            Assert.NotNull(notice.QuerySelector("svg.icon"));
        }
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
    public void ActionsMenu_IsShownOnlyWhenTheRecordCanBeEdited_AndOffersEditAndDelete()
    {
        var editable = Samples.Record(canEdit: true);
        var cut = RenderView(editable);
        Assert.Empty(cut.FindAll("#record-actions-menu"));

        cut.Find("#record-actions-button").Click();

        Assert.Equal($"/records/{editable.Id}/edit", cut.Find("a#edit-record").GetAttribute("href"));
        Assert.Equal("Delete", cut.Find("button#delete-record").TextContent.Trim());

        var readOnly = Samples.Record(canEdit: false);
        var other = RenderView(readOnly);
        Assert.Empty(other.FindAll("#record-actions-button"));
    }

    [Fact]
    public void Delete_AsksForConfirmation_ThenDeletesTheRecordAndLeavesThePage()
    {
        var record = Samples.Record(canEdit: true);
        _api.On(HttpMethod.Delete, $"api/records/{record.Id}", _ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var cut = RenderView(record);

        cut.Find("#record-actions-button").Click();
        cut.Find("button#delete-record").Click();
        Assert.DoesNotContain(_api.Calls, c => c.Method == HttpMethod.Delete);
        cut.Find("#confirm-delete-record").Click();

        cut.WaitForAssertion(() => Assert.Contains(_api.Calls, c => c.Method == HttpMethod.Delete && c.PathAndQuery.EndsWith($"api/records/{record.Id}", StringComparison.Ordinal)));
        Assert.EndsWith("/records", Services.GetRequiredService<NavigationManager>().Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownRecord_ShowsNotFound()
    {
        var id = Guid.NewGuid();
        _api.On(HttpMethod.Get, $"api/records/{id}", _ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var cut = Render<RecordView>(p => p.Add(c => c.Id, id));

        Assert.Contains("not found", cut.Find("[role=alert]").TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(cut.FindAll("#engagement-period"));
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
        Assert.Contains("<script>", cut.Find("nav[aria-label=breadcrumb] li.active").TextContent, StringComparison.Ordinal);
    }
}
