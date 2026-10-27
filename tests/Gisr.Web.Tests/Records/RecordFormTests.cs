using System.Text.Json;
using Bunit;
using Gisr.Shared.Common;
using Gisr.Shared.Records;
using Gisr.Web.Common;
using Gisr.Web.Records;
using Gisr.Web.Tests.Common;
using Microsoft.AspNetCore.Components;

namespace Gisr.Web.Tests.Records;

public class RecordFormTests : BunitContext
{
    private readonly FakeApi _api = new();
    private RecordFormModel _model = RecordFormModel.New("Military");
    private Func<RecordFormModel, Task> _save = _ => Task.CompletedTask;
    private Func<Task>? _delete;
    private int _reloads;

    public RecordFormTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _api.AddTo(Services);
    }

    private static ApiException Problem(int status, params (string Field, string Message)[] errors) =>
        new(status, "Problem", null,
            errors.ToDictionary(e => e.Field, e => new[] { e.Message }, StringComparer.Ordinal),
            new Dictionary<string, JsonElement>(), null);

    private static RecordFormModel Saved(int version = 2) => RecordFormModel.FromDto(Samples.Record(version: version));

    private IRenderedComponent<RecordForm> RenderForm() =>
        Render<RecordForm>(p => p
            .Add(c => c.Model, _model)
            .Add(c => c.Affiliations, Samples.Affiliations)
            .Add(c => c.OnSave, _save)
            .Add(c => c.OnDelete, _delete)
            .Add(c => c.CancelHref, "/records")
            .Add(c => c.OnReload, EventCallback.Factory.Create(this, () => _reloads++)));

    [Fact]
    public void TheFormIsACardWithAHeaderAndFooter()
    {
        var cut = RenderForm();

        var card = cut.Find("div#record-card.card");
        Assert.Equal("Citizen Record", card.QuerySelector(":scope > .card-header")!.TextContent.Trim());
        Assert.NotNull(card.QuerySelector(":scope > .card-body"));
        Assert.NotNull(card.QuerySelector(":scope > .card-footer"));
    }

    [Fact]
    public void TheFooterHasSaveDeleteAndCancel_EachButtonWithAnIcon()
    {
        var cut = RenderForm();

        var footer = cut.Find(".card-footer");
        var save = footer.QuerySelector("#save-record")!;
        var delete = footer.QuerySelector("#delete-record")!;
        var cancel = footer.QuerySelector("#cancel-record")!;
        Assert.Equal("Save Record", save.TextContent.Trim());
        Assert.NotNull(save.QuerySelector("svg.icon"));
        Assert.Equal("submit", save.GetAttribute("type"));
        Assert.Equal("record-form", save.GetAttribute("form"));
        Assert.Equal("Delete record", delete.TextContent.Trim());
        Assert.NotNull(delete.QuerySelector("svg.icon"));
        Assert.Equal("Cancel", cancel.TextContent.Trim());
        Assert.Equal("/records", cancel.GetAttribute("href"));
    }

    [Fact]
    public void TheTabsAreListedInOrder_WithRecordDetailsSelectedFirst()
    {
        _model = Saved();
        var cut = RenderForm();

        var tabs = cut.FindAll("[role=tablist] [role=tab]");

        Assert.Equal(["Record Details", "Incidents", "Awards", "Artifacts", "History"], tabs.Select(t => t.TextContent.Trim()).ToArray());
        Assert.Equal(["true", "false", "false", "false", "false"], tabs.Select(t => t.GetAttribute("aria-selected")!).ToArray());
        Assert.Contains("active", cut.Find("#pane-details").ClassList);
        Assert.DoesNotContain("active", cut.Find("#pane-incidents").ClassList);
    }

    [Fact]
    public void BeforeTheFirstSave_OnlyRecordDetailsAndHistoryAreShown()
    {
        var cut = RenderForm();

        var tabs = cut.FindAll("[role=tablist] [role=tab]");

        Assert.Equal(["Record Details", "History"], tabs.Select(t => t.TextContent.Trim()).ToArray());
        Assert.Empty(cut.FindAll("#tab-incidents, #tab-awards, #tab-artifacts"));
        Assert.Empty(cut.FindAll("#pane-incidents, #pane-awards, #pane-artifacts"));
        Assert.Empty(cut.FindAll("[role=tab][disabled]"));
        Assert.Empty(cut.FindAll("#add-incident"));
        Assert.Empty(cut.FindAll("#add-award"));
        Assert.Empty(cut.FindAll("#artifact-uploader"));
    }

    [Fact]
    public void AfterTheFirstSave_TheTabsAreEnabled_AndSwitchContent()
    {
        _model = Saved();
        var cut = RenderForm();

        Assert.Empty(cut.FindAll("[role=tab][disabled]"));
        Assert.Empty(cut.FindAll("[role=tablist] [title]"));

        cut.Find("#tab-incidents").Click();
        Assert.Equal("true", cut.Find("#tab-incidents").GetAttribute("aria-selected"));
        Assert.Contains("active", cut.Find("#pane-incidents").ClassList);
        Assert.DoesNotContain("active", cut.Find("#pane-details").ClassList);
        Assert.NotNull(cut.Find("#add-incident"));

        cut.Find("#tab-awards").Click();
        Assert.Contains("active", cut.Find("#pane-awards").ClassList);
        Assert.NotNull(cut.Find("#add-award"));

        cut.Find("#tab-artifacts").Click();
        Assert.Contains("active", cut.Find("#pane-artifacts").ClassList);
        Assert.NotNull(cut.Find("#artifact-uploader"));
    }

    [Fact]
    public void TheArtifactsTab_ShowsTheUploaderBeforeTheAddedArtifacts()
    {
        _model = Saved();
        var cut = RenderForm();

        var pane = cut.Find("#pane-artifacts").InnerHtml;

        Assert.True(pane.IndexOf("artifact-uploader", StringComparison.Ordinal) < pane.IndexOf("Added artifacts", StringComparison.Ordinal));
    }

    [Fact]
    public void TheHistoryTab_LoadsTheRevisionsWhenOpened()
    {
        _model = Saved(version: 2);
        _api.OnJson(HttpMethod.Get, $"api/records/{_model.Id}/history?page=1&pageSize=20", new PagedResult<RevisionDto>(1, 20, 1,
            [new RevisionDto(2, "Rex", new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero), "Military", "Renamed")]));
        var cut = RenderForm();
        Assert.DoesNotContain(_api.Calls, c => c.PathAndQuery.Contains("history", StringComparison.Ordinal));

        cut.Find("#tab-history").Click();

        cut.WaitForAssertion(() => Assert.Contains("Renamed", cut.Find("#pane-history").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void TheHistoryTab_OfAnUnsavedRecord_SaysThereIsNoneYet()
    {
        var cut = RenderForm();

        cut.Find("#tab-history").Click();

        Assert.NotNull(cut.Find("#history-unsaved"));
        Assert.Empty(_api.Calls);
    }

    [Fact]
    public void TheHistoryMessage_OfAnUnsavedRecord_IsAnInfoAlertWithAnIcon()
    {
        var cut = RenderForm();

        cut.Find("#tab-history").Click();

        var history = cut.Find("#history-unsaved");
        Assert.Contains("alert-info", history.ClassList);
        Assert.NotNull(history.QuerySelector("svg.icon"));
        Assert.Equal("History appears after the record is saved.", history.TextContent.Trim());
    }

    [Fact]
    public void AffiliationSelect_ListsOnlyTheUsersAccountAffiliations()
    {
        var cut = RenderForm();

        var options = cut.FindAll("select#record-affiliation option").Select(o => o.TextContent.Trim()).ToArray();

        Assert.Equal(["Select an affiliation", "Military", "Jedi"], options);
        Assert.Empty(cut.FindAll("#record-affiliation-help"));
    }

    [Fact]
    public void AffiliationOptions_UsePrimaryAndSecondary_NotTheSpecialUnit()
    {
        var cut = Render<RecordForm>(p => p
            .Add(c => c.Model, RecordFormModel.New(string.Empty))
            .Add(c => c.Affiliations, new[]
            {
                new Gisr.Shared.Accounts.AccountAffiliationDto(Guid.NewGuid(), "Jedi Order", "Jedi Praxeum", "Temple Guard", null, null, DateTimeOffset.UtcNow),
                new Gisr.Shared.Accounts.AccountAffiliationDto(Guid.NewGuid(), "Rebel Alliance", null, null, null, null, DateTimeOffset.UtcNow),
            })
            .Add(c => c.OnSave, _save));

        var options = cut.FindAll("select#record-affiliation option").Select(o => o.GetAttribute("value")!).ToArray();

        Assert.Equal(["", "Jedi Order \u203A Jedi Praxeum", "Rebel Alliance"], options);
    }

    [Fact]
    public void AffiliationSelect_KeepsTheRecordsCurrentValueEvenWhenItIsNotTheUsers()
    {
        _model = RecordFormModel.FromDto(Samples.Record(affiliation: "Sith Order"));
        var cut = RenderForm();

        var options = cut.FindAll("select#record-affiliation option").Select(o => o.GetAttribute("value")!).ToArray();

        Assert.Equal(["", "Sith Order", "Military", "Jedi"], options);
    }

    [Fact]
    public void WithoutAffiliations_PointsTheUserToAccountSettings()
    {
        var cut = Render<RecordForm>(p => p
            .Add(c => c.Model, RecordFormModel.New(string.Empty))
            .Add(c => c.Affiliations, Array.Empty<Gisr.Shared.Accounts.AccountAffiliationDto>())
            .Add(c => c.OnSave, _save));

        Assert.Equal("/account", cut.Find("#record-affiliation-help a").GetAttribute("href"));
    }

    [Fact]
    public void ChangingTheAffiliation_UpdatesTheModel()
    {
        var cut = RenderForm();

        cut.Find("select#record-affiliation").Change("Jedi");

        Assert.Equal("Jedi", _model.Affiliation);
    }

    [Fact]
    public void RecordDetails_AreTitleAffiliationEngagementDatesAndSummaryOnly()
    {
        var cut = RenderForm();

        var pane = cut.Find("#pane-details");

        Assert.NotNull(pane.QuerySelector("#record-title"));
        Assert.NotNull(pane.QuerySelector("#record-affiliation"));
        Assert.NotNull(pane.QuerySelector("#engagement-start"));
        Assert.NotNull(pane.QuerySelector("#engagement-end"));
        Assert.Equal("TEXTAREA", pane.QuerySelector("#detail-summary")!.TagName);
        Assert.Equal("true", pane.QuerySelector("#detail-summary")!.GetAttribute("aria-required"));
        foreach (var gone in new[] { "#detail-branch", "#detail-rank", "#detail-unit", "#detail-service_number" })
        {
            Assert.Empty(pane.QuerySelectorAll(gone));
        }

        Assert.Equal("true", cut.Find("#record-title").GetAttribute("aria-required"));
    }

    [Fact]
    public void Submit_PassesTheEnteredValuesToOnSave()
    {
        RecordFormModel? saved = null;
        _save = m =>
        {
            saved = m;
            return Task.CompletedTask;
        };
        var cut = RenderForm();

        cut.Find("#record-title").Change("Temple guard");
        cut.Find("select#record-affiliation").Change("Jedi");
        cut.Find("#engagement-start").Change("2019-03-04");
        cut.Find("#detail-summary").Change("Guarded the temple.");
        cut.Find("form").Submit();

        Assert.NotNull(saved);
        Assert.Equal("Temple guard", saved.Title);
        Assert.Equal(new DateOnly(2019, 3, 4), saved.EngagementStart);
        var request = saved.ToCreateRequest();
        Assert.Equal("Jedi", request.Affiliation);
        Assert.Equal(new Dictionary<string, string> { ["summary"] = "Guarded the temple." }, request.EngagementDetail);
    }

    [Fact]
    public void Model_FromDto_KeepsOnlyTheSummary()
    {
        var dto = Samples.Record(version: 7) with
        {
            EngagementDetail = new Dictionary<string, string> { ["summary"] = "Served", ["rank"] = "Captain", ["branch"] = "Navy" },
        };

        var model = RecordFormModel.FromDto(dto);

        Assert.Equal(7, model.ToUpdateRequest().ExpectedVersion);
        Assert.Equal(new Dictionary<string, string> { ["summary"] = "Served" }, model.ToUpdateRequest().EngagementDetail);
        Assert.Equal("Geonosis", model.Incidents.Single().Title);
        Assert.Equal("Medal of Honor", model.Awards.Single().Name);
        Assert.True(model.IsSaved);
        Assert.False(RecordFormModel.New("Military").IsSaved);
    }

    [Fact]
    public void ValidationFailure_ShowsInlineErrorsLinkedToFields_AndKeepsInput()
    {
        _save = _ => throw Problem(400,
            ("title", "'Title' must not be empty."),
            ("engagementDetail.summary", "'Summary' is required."));
        _model.Title = "";
        var cut = RenderForm();
        cut.Find("#engagement-end").Change("2020-01-01");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            var title = cut.Find("#record-title");
            Assert.Equal("true", title.GetAttribute("aria-invalid"));
            Assert.Equal("record-title-error", title.GetAttribute("aria-describedby"));
            Assert.Contains("must not be empty", cut.Find("#record-title-error").TextContent, StringComparison.Ordinal);
            var summary = cut.Find("#detail-summary");
            Assert.Equal("true", summary.GetAttribute("aria-invalid"));
            Assert.Equal("detail-summary-error", summary.GetAttribute("aria-describedby"));
            Assert.Contains("is required", cut.Find("#detail-summary-error").TextContent, StringComparison.Ordinal);
            Assert.Contains("fix the highlighted", cut.Find("[role=alert]").TextContent, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("2020-01-01", cut.Find("#engagement-end").GetAttribute("value"));
        });
    }

    [Fact]
    public void ErrorsInAnotherTab_SwitchToThatTab()
    {
        _model = Saved();
        _model.Incidents[0].Title = "";
        _save = _ => throw Problem(400, ("incidents[0].title", "'Title' must not be empty."));
        var cut = RenderForm();

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("active", cut.Find("#pane-incidents").ClassList);
            Assert.Equal("incident-0-title-error", cut.Find("#incident-0-title").GetAttribute("aria-describedby"));
        });
    }

    [Fact]
    public void VersionConflict_ShowsAlert_PreservesInput_AndOffersReload()
    {
        _save = _ => throw Problem(409);
        _model.Title = "My unsaved edit";
        var cut = RenderForm();

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            var alert = cut.Find("#conflict-alert");
            Assert.Equal("alert", alert.GetAttribute("role"));
            Assert.Contains("changed", alert.TextContent, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("My unsaved edit", cut.Find("#record-title").GetAttribute("value"));
        });

        cut.Find("#conflict-alert button").Click();
        Assert.Equal(1, _reloads);
    }

    [Fact]
    public void UnexpectedFailure_ShowsGenericError()
    {
        _save = _ => throw Problem(502);
        var cut = RenderForm();

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
            Assert.Contains("could not be saved", cut.Find("[role=alert]").TextContent, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Incidents_NewItemFormComesFirst_AndAddedItemsAreListedAfterIt()
    {
        _model = Saved();
        var cut = RenderForm();
        cut.Find("#tab-incidents").Click();

        cut.Find("#new-incident-date").Change("2021-02-03");
        cut.Find("#new-incident-title").Change("Umbara");
        cut.Find("#add-incident").Click();

        Assert.Equal(["Geonosis", "Umbara"], _model.Incidents.Select(i => i.Title));
        var pane = cut.Find("#pane-incidents").InnerHtml;
        Assert.True(pane.IndexOf("new-incident-title", StringComparison.Ordinal) < pane.IndexOf("incident-0-title", StringComparison.Ordinal));
        Assert.Equal("Umbara", cut.Find("#incident-1-title").GetAttribute("value"));
        Assert.Equal(string.Empty, cut.Find("#new-incident-title").GetAttribute("value") ?? string.Empty);
    }

    [Fact]
    public void Incidents_AnIncompleteNewItemIsNotAdded_AndShowsErrors()
    {
        _model = Saved();
        var cut = RenderForm();
        cut.Find("#tab-incidents").Click();

        cut.Find("#add-incident").Click();

        Assert.Single(_model.Incidents);
        Assert.Equal("new-incident-date-error", cut.Find("#new-incident-date").GetAttribute("aria-describedby"));
        Assert.Equal("new-incident-title-error", cut.Find("#new-incident-title").GetAttribute("aria-describedby"));
    }

    [Fact]
    public void Incidents_CanBeRemoved_WithAccessibleControls()
    {
        _model = Saved();
        _model.Incidents.Add(new IncidentModel { OccurredOn = new DateOnly(2021, 1, 1), Title = "Umbara" });
        var cut = RenderForm();
        cut.Find("#tab-incidents").Click();

        cut.Find("button[aria-label='Remove incident 1']").Click();

        Assert.Equal("Umbara", Assert.Single(_model.Incidents).Title);
        Assert.Equal("Umbara", cut.Find("#incident-0-title").GetAttribute("value"));
        Assert.Empty(cut.FindAll("#incident-1-title"));
    }

    [Fact]
    public void Awards_NewItemFormComesFirst_AndAddedItemsAreListedAfterIt()
    {
        _model = Saved();
        var cut = RenderForm();
        cut.Find("#tab-awards").Click();

        cut.Find("#new-award-name").Change("Star");
        cut.Find("#new-award-date").Change("2022-03-04");
        cut.Find("#new-award-issuer").Change("Senate");
        cut.Find("#add-award").Click();

        Assert.Equal(["Medal of Honor", "Star"], _model.Awards.Select(a => a.Name));
        Assert.Equal("Senate", _model.Awards[1].Issuer);
        var pane = cut.Find("#pane-awards").InnerHtml;
        Assert.True(pane.IndexOf("new-award-name", StringComparison.Ordinal) < pane.IndexOf("award-0-name", StringComparison.Ordinal));
    }

    [Fact]
    public void Awards_AnIncompleteNewItemIsNotAdded_AndShowsErrors()
    {
        _model = Saved();
        var cut = RenderForm();
        cut.Find("#tab-awards").Click();

        cut.Find("#add-award").Click();

        Assert.Single(_model.Awards);
        Assert.Equal("new-award-name-error", cut.Find("#new-award-name").GetAttribute("aria-describedby"));
        Assert.Equal("new-award-date-error", cut.Find("#new-award-date").GetAttribute("aria-describedby"));
    }

    [Fact]
    public void Awards_CanBeRemoved_WithAccessibleControls()
    {
        _model = Saved();
        var cut = RenderForm();
        cut.Find("#tab-awards").Click();

        cut.Find("button[aria-label='Remove award 1']").Click();

        Assert.Empty(_model.Awards);
        Assert.NotNull(cut.Find("#awards-empty"));
    }

    [Fact]
    public void AddButtons_AreDisabledAtTheLimitOf100()
    {
        _model = Saved();
        for (var i = 0; i < 100; i++)
        {
            _model.Incidents.Add(new IncidentModel { Title = $"i{i}" });
            _model.Awards.Add(new AwardModel { Name = $"a{i}" });
        }

        var cut = RenderForm();

        Assert.True(cut.Find("#add-incident").HasAttribute("disabled"));
        Assert.True(cut.Find("#add-award").HasAttribute("disabled"));
    }

    [Fact]
    public void IncidentAndAwardFields_MapToTheRequest()
    {
        _model = Saved();
        _model.Incidents.Clear();
        _model.Awards.Clear();
        var cut = RenderForm();

        cut.Find("#tab-incidents").Click();
        cut.Find("#new-incident-date").Change("2020-05-04");
        cut.Find("#new-incident-title").Change("Geonosis");
        cut.Find("#new-incident-description").Change("First battle");
        cut.Find("#add-incident").Click();
        cut.Find("#tab-awards").Click();
        cut.Find("#new-award-name").Change("Medal");
        cut.Find("#new-award-date").Change("2021-01-02");
        cut.Find("#new-award-issuer").Change("Republic");
        cut.Find("#new-award-description").Change("For valor");
        cut.Find("#add-award").Click();

        var request = _model.ToUpdateRequest();
        Assert.Equal(new IncidentDto(null, new DateOnly(2020, 5, 4), "Geonosis", "First battle"), request.Incidents!.Single());
        Assert.Equal(new AwardDto(null, "Medal", new DateOnly(2021, 1, 2), "Republic", "For valor"), request.Awards!.Single());
    }

    [Fact]
    public void Delete_IsDisabledUntilTheRecordIsSaved()
    {
        _delete = () => Task.CompletedTask;
        var cut = RenderForm();

        Assert.True(cut.Find("#delete-record").HasAttribute("disabled"));
    }

    [Fact]
    public void Delete_AsksForConfirmation_ThenCallsOnDelete()
    {
        var deleted = 0;
        _delete = () =>
        {
            deleted++;
            return Task.CompletedTask;
        };
        _model = Saved();
        var cut = RenderForm();

        cut.Find("#delete-record").Click();
        Assert.Equal(0, deleted);
        Assert.NotNull(cut.Find("#delete-confirmation"));

        cut.Find("#confirm-delete-record").Click();

        Assert.Equal(1, deleted);
    }

    [Fact]
    public void Delete_CanBeCancelled()
    {
        var deleted = 0;
        _delete = () =>
        {
            deleted++;
            return Task.CompletedTask;
        };
        _model = Saved();
        var cut = RenderForm();

        cut.Find("#delete-record").Click();
        cut.Find("#cancel-delete-record").Click();

        Assert.Empty(cut.FindAll("#delete-confirmation"));
        Assert.Equal(0, deleted);
    }

    [Fact]
    public void Delete_Failure_ShowsAnError()
    {
        _delete = () => throw Problem(500);
        _model = Saved();
        var cut = RenderForm();

        cut.Find("#delete-record").Click();
        cut.Find("#confirm-delete-record").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("could not be deleted", cut.Find("#delete-failed").TextContent, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(cut.FindAll("#delete-confirmation"));
        });
    }
}
