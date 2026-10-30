using System.Net;
using Bunit;
using Gisr.Shared.Records;
using Gisr.Web.Records;
using Gisr.Web.Tests.Common;

namespace Gisr.Web.Tests.Records;

public class SummaryEnhancerTests : BunitContext
{
    private const string Path = "api/records/summary/enhance";

    private readonly FakeApi _api = new();
    private readonly RecordFormModel _model = RecordFormModel.New("Military");

    public SummaryEnhancerTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _api.AddTo(Services);
    }

    private IRenderedComponent<RecordForm> RenderForm(bool allow = true) =>
        Render<RecordForm>(p => p
            .Add(c => c.Model, _model)
            .Add(c => c.Affiliations, Samples.Affiliations)
            .Add(c => c.OnSave, _ => Task.CompletedTask)
            .Add(c => c.AllowSummaryEnhancement, allow));

    [Fact]
    public void TheButton_IsHidden_UnlessEnhancementIsAllowed()
    {
        var cut = RenderForm(allow: false);

        Assert.Empty(cut.FindAll("#enhance-summary"));
    }

    [Fact]
    public void TheButton_IsShown_UnderTheSummary_WithAnAccessibleDescription()
    {
        var cut = RenderForm();

        var button = cut.Find("#enhance-summary");
        Assert.Equal("button", button.GetAttribute("type"));
        Assert.Contains("Enhance with AI", button.TextContent, StringComparison.Ordinal);
        Assert.NotNull(cut.Find($"#{button.GetAttribute("aria-describedby")}"));
    }

    [Fact]
    public void Clicking_SendsTheDraft_AndReplacesTheSummaryWithTheResult()
    {
        _model.Summary = "served at kamino";
        _api.OnJson(HttpMethod.Post, Path, new EnhancedSummaryDto("Served at Kamino with distinction."));
        var cut = RenderForm();

        cut.Find("#enhance-summary").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Served at Kamino with distinction.", _model.Summary);
            Assert.Contains("Review it before saving", cut.Find("#enhance-summary-message").TextContent, StringComparison.Ordinal);
        });
        var call = Assert.Single(_api.Calls, c => c.PathAndQuery == Path);
        Assert.Contains("served at kamino", call.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Undo_RestoresTheOriginalSummary()
    {
        _model.Summary = "original";
        _api.OnJson(HttpMethod.Post, Path, new EnhancedSummaryDto("better"));
        var cut = RenderForm();
        cut.Find("#enhance-summary").Click();
        cut.WaitForAssertion(() => Assert.Equal("better", _model.Summary));

        cut.Find("#undo-enhance-summary").Click();

        Assert.Equal("original", _model.Summary);
        Assert.Empty(cut.FindAll("#undo-enhance-summary"));
    }

    [Fact]
    public void ABlankSummary_AsksForText_WithoutCallingTheApi()
    {
        var cut = RenderForm();

        cut.Find("#enhance-summary").Click();

        Assert.Equal("alert", cut.Find("#enhance-summary-message").GetAttribute("role"));
        Assert.DoesNotContain(_api.Calls, c => c.PathAndQuery == Path);
    }

    [Fact]
    public void WhenTheServiceIsUnavailable_ShowsAnErrorAndKeepsTheSummary()
    {
        _model.Summary = "keep me";
        _api.On(HttpMethod.Post, Path, _ => FakeApi.Problem(HttpStatusCode.ServiceUnavailable, new { title = "Service Unavailable", status = 503 }));
        var cut = RenderForm();

        cut.Find("#enhance-summary").Click();

        cut.WaitForAssertion(() =>
        {
            var message = cut.Find("#enhance-summary-message");
            Assert.Equal("alert", message.GetAttribute("role"));
            Assert.Contains("unavailable", message.TextContent, StringComparison.Ordinal);
        });
        Assert.Equal("keep me", _model.Summary);
        Assert.Empty(cut.FindAll("#undo-enhance-summary"));
    }
}
