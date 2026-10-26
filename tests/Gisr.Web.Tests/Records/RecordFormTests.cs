using System.Text.Json;
using Bunit;
using Gisr.Shared.Records;
using Gisr.Web.Common;
using Gisr.Web.Records;
using Microsoft.AspNetCore.Components;

namespace Gisr.Web.Tests.Records;

public class RecordFormTests : BunitContext
{
    private RecordFormModel _model = RecordFormModel.New("military");
    private Func<RecordFormModel, Task> _save = _ => Task.CompletedTask;
    private int _reloads;

    private static ApiException Problem(int status, params (string Field, string Message)[] errors) =>
        new(status, "Problem", null,
            errors.ToDictionary(e => e.Field, e => new[] { e.Message }, StringComparer.Ordinal),
            new Dictionary<string, JsonElement>(), null);

    private IRenderedComponent<RecordForm> RenderForm() =>
        Render<RecordForm>(p => p
            .Add(c => c.Model, _model)
            .Add(c => c.Affiliations, Samples.Affiliations)
            .Add(c => c.OnSave, _save)
            .Add(c => c.OnReload, EventCallback.Factory.Create(this, () => _reloads++)));

    [Fact]
    public void AffiliationSelect_ListsEveryAffiliation()
    {
        var cut = RenderForm();

        var options = cut.FindAll("select#record-affiliation option").Select(o => o.TextContent.Trim()).ToArray();

        Assert.Equal(["Military", "Jedi"], options);
    }

    [Fact]
    public void Fields_RenderFromTheSelectedAffiliationDefinition()
    {
        var cut = RenderForm();

        Assert.Equal("TEXTAREA", cut.Find("#detail-summary").TagName);
        Assert.Equal("SELECT", cut.Find("#detail-branch").TagName);
        Assert.Equal("INPUT", cut.Find("#detail-rank").TagName);
        Assert.Equal(["", "Navy", "Army"], cut.FindAll("#detail-branch option").Select(o => o.GetAttribute("value") ?? "").ToArray());
        Assert.Empty(cut.FindAll("#detail-order_rank"));
    }

    [Fact]
    public void ChangingAffiliation_RerendersItsFields()
    {
        var cut = RenderForm();

        cut.Find("select#record-affiliation").Change("jedi");

        Assert.NotNull(cut.Find("#detail-order_rank"));
        Assert.NotNull(cut.Find("#detail-mentor"));
        Assert.Empty(cut.FindAll("#detail-rank"));
        Assert.Equal("jedi", _model.Affiliation);
    }

    [Fact]
    public void ChangingAffiliation_KeepsValuesOfTheOtherAffiliation()
    {
        _model.Detail["rank"] = "Captain";
        var cut = RenderForm();

        cut.Find("select#record-affiliation").Change("jedi");
        cut.Find("select#record-affiliation").Change("military");

        Assert.Equal("Captain", cut.Find("#detail-rank").GetAttribute("value"));
    }

    [Fact]
    public void RequiredFields_AreMarked_AndOptionalFieldsAreNot()
    {
        var cut = RenderForm();

        Assert.Equal("true", cut.Find("#detail-rank").GetAttribute("aria-required"));
        Assert.Contains("*", cut.Find("label[for='detail-rank']").TextContent, StringComparison.Ordinal);
        Assert.Equal("true", cut.Find("#record-title").GetAttribute("aria-required"));
        Assert.Null(cut.Find("#detail-unit").GetAttribute("aria-required"));
        Assert.DoesNotContain("*", cut.Find("label[for='detail-unit']").TextContent, StringComparison.Ordinal);
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
        cut.Find("#engagement-start").Change("2019-03-04");
        cut.Find("#detail-summary").Change("Guarded the temple.");
        cut.Find("#detail-branch").Change("Army");
        cut.Find("#detail-rank").Change("Sergeant");
        cut.Find("form").Submit();

        Assert.NotNull(saved);
        Assert.Equal("Temple guard", saved.Title);
        Assert.Equal(new DateOnly(2019, 3, 4), saved.EngagementStart);
        Assert.Equal("Sergeant", saved.Detail["rank"]);
        var request = saved.ToCreateRequest();
        Assert.Equal("Army", request.EngagementDetail!["branch"]);
    }

    [Fact]
    public void Submit_Sends_ExpectedVersion_ForUpdates()
    {
        var model = RecordFormModel.FromDto(Samples.Record(version: 7));

        Assert.Equal(7, model.ToUpdateRequest().ExpectedVersion);
        Assert.Equal("Captain", model.Detail["rank"]);
        Assert.Equal("Geonosis", model.Incidents.Single().Title);
        Assert.Equal("Medal of Honor", model.Awards.Single().Name);
    }

    [Fact]
    public void ValidationFailure_ShowsInlineErrorsLinkedToFields_AndKeepsInput()
    {
        _save = _ => throw Problem(400,
            ("title", "'Title' must not be empty."),
            ("engagementDetail.rank", "'Rank' is required."),
            ("incidents[0].title", "'Title' must not be empty."));
        _model.Incidents.Add(new IncidentModel { OccurredOn = new DateOnly(2020, 1, 1), Title = "" });
        var cut = RenderForm();
        cut.Find("#detail-unit").Change("Torrent Company");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            var title = cut.Find("#record-title");
            Assert.Equal("true", title.GetAttribute("aria-invalid"));
            Assert.Equal("record-title-error", title.GetAttribute("aria-describedby"));
            Assert.Contains("must not be empty", cut.Find("#record-title-error").TextContent, StringComparison.Ordinal);
            var rank = cut.Find("#detail-rank");
            Assert.Equal("true", rank.GetAttribute("aria-invalid"));
            Assert.Equal("detail-rank-error", rank.GetAttribute("aria-describedby"));
            Assert.Contains("is required", cut.Find("#detail-rank-error").TextContent, StringComparison.Ordinal);
            Assert.Equal("incident-0-title-error", cut.Find("#incident-0-title").GetAttribute("aria-describedby"));
            Assert.Contains("fix the highlighted", cut.Find("[role=alert]").TextContent, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("Torrent Company", cut.Find("#detail-unit").GetAttribute("value"));
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
    public void Incidents_CanBeAddedAndRemoved_WithAccessibleControls()
    {
        var cut = RenderForm();

        cut.Find("#add-incident").Click();
        cut.Find("#add-incident").Click();
        cut.Find("#incident-0-title").Change("Geonosis");
        cut.Find("#incident-1-title").Change("Umbara");

        Assert.Equal(2, _model.Incidents.Count);
        cut.Find("button[aria-label='Remove incident 1']").Click();

        Assert.Single(_model.Incidents);
        Assert.Equal("Umbara", cut.Find("#incident-0-title").GetAttribute("value"));
        Assert.Empty(cut.FindAll("#incident-1-title"));
    }

    [Fact]
    public void Awards_CanBeAddedAndRemoved_WithAccessibleControls()
    {
        var cut = RenderForm();

        cut.Find("#add-award").Click();
        cut.Find("#add-award").Click();
        cut.Find("#award-0-name").Change("Medal");
        cut.Find("#award-1-name").Change("Star");
        cut.Find("button[aria-label='Remove award 2']").Click();

        Assert.Single(_model.Awards);
        Assert.Equal("Medal", cut.Find("#award-0-name").GetAttribute("value"));
    }

    [Fact]
    public void AddButtons_AreDisabledAtTheLimitOf100()
    {
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
        var cut = RenderForm();
        cut.Find("#add-incident").Click();
        cut.Find("#add-award").Click();

        cut.Find("#incident-0-date").Change("2020-05-04");
        cut.Find("#incident-0-title").Change("Geonosis");
        cut.Find("#incident-0-description").Change("First battle");
        cut.Find("#award-0-name").Change("Medal");
        cut.Find("#award-0-date").Change("2021-01-02");
        cut.Find("#award-0-issuer").Change("Republic");
        cut.Find("#award-0-description").Change("For valor");

        var request = _model.ToCreateRequest();
        Assert.Equal(new IncidentDto(null, new DateOnly(2020, 5, 4), "Geonosis", "First battle"), request.Incidents!.Single());
        Assert.Equal(new AwardDto(null, "Medal", new DateOnly(2021, 1, 2), "Republic", "For valor"), request.Awards!.Single());
    }
}
