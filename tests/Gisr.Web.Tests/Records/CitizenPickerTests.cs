using System.Net;
using System.Text.Json;
using Bunit;
using Gisr.Shared.Accounts;
using Gisr.Shared.Records;
using Gisr.Web.Records;
using Gisr.Web.Tests.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Gisr.Web.Tests.Records;

public class CitizenPickerTests : BunitContext
{
    private readonly FakeApi _api = new();
    private readonly RecordFormModel _model = RecordFormModel.New("military");

    public CitizenPickerTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _api.OnJson(HttpMethod.Get, "api/affiliations", Samples.Affiliations);
        _api.AddTo(Services);
    }

    private IRenderedComponent<CitizenPicker> RenderPicker() => Render<CitizenPicker>(p => p.Add(c => c.Model, _model));

    private static string Search(string query) => $"api/accounts?query={Uri.EscapeDataString(query)}&pageSize=10";

    [Fact]
    public void DefaultsToMyself_WithNoResults()
    {
        var cut = RenderPicker();

        Assert.Equal("Myself", cut.Find("#citizen-selected").TextContent.Trim());
        Assert.Empty(cut.FindAll("button.citizen-option"));
        Assert.Null(_model.SubjectObjectId);
    }

    [Fact]
    public void HasAnAccessibleLabelForTheSearchBox()
    {
        var cut = RenderPicker();

        var input = cut.Find("input#citizen-search");
        Assert.Equal("Citizen", cut.Find($"label[for={input.Id}]").TextContent.Trim());
    }

    [Fact]
    public void ShortQueries_DoNotCallTheApi()
    {
        var cut = RenderPicker();

        cut.Find("#citizen-search").Input("l");

        Assert.DoesNotContain(_api.Calls, c => c.PathAndQuery.StartsWith("api/accounts", StringComparison.Ordinal));
    }

    [Fact]
    public void Typing_SearchesTheAccountDirectory_AndListsTheMatches()
    {
        _api.OnJson(HttpMethod.Get, Search("lu"), new[]
        {
            new DirectoryUserDto(Guid.NewGuid(), "Luke Skywalker", null),
            new DirectoryUserDto(Guid.NewGuid(), "Lucius Fox", "lucius@wayne.example"),
        });
        var cut = RenderPicker();

        cut.Find("#citizen-search").Input("lu");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(["Luke Skywalker", "Lucius Fox"], cut.FindAll("button.citizen-option > span").Select(s => s.TextContent.Trim()).ToArray());
            Assert.Contains("lucius@wayne.example", cut.FindAll("button.citizen-option")[1].TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void QueryIsEscapedInTheRequest()
    {
        _api.OnJson(HttpMethod.Get, Search("a&b c"), Array.Empty<DirectoryUserDto>());
        var cut = RenderPicker();

        cut.Find("#citizen-search").Input("a&b c");

        cut.WaitForAssertion(() => Assert.Contains(_api.Calls, c => c.PathAndQuery == Search("a&b c")));
    }

    [Fact]
    public void ChoosingACitizen_SetsTheSubject_ThenMyselfResetsIt()
    {
        var luke = new DirectoryUserDto(Guid.NewGuid(), "Luke Skywalker", null);
        _api.OnJson(HttpMethod.Get, Search("lu"), new[] { luke });
        var cut = RenderPicker();
        cut.Find("#citizen-search").Input("lu");
        cut.WaitForAssertion(() => cut.Find("button.citizen-option").Click());

        Assert.Equal(luke.ObjectId, _model.SubjectObjectId);
        Assert.Equal("Luke Skywalker", cut.Find("#citizen-selected").TextContent.Trim());
        Assert.Empty(cut.FindAll("button.citizen-option"));

        cut.Find("#citizen-myself").Click();

        Assert.Null(_model.SubjectObjectId);
        Assert.Equal("Myself", cut.Find("#citizen-selected").TextContent.Trim());
    }

    [Fact]
    public void NoMatches_ShowsAMessage()
    {
        _api.OnJson(HttpMethod.Get, Search("zz"), Array.Empty<DirectoryUserDto>());
        var cut = RenderPicker();

        cut.Find("#citizen-search").Input("zz");

        cut.WaitForAssertion(() => Assert.Contains("No matching citizens", cut.Find("#citizen-results-status").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void SearchFailure_ShowsAMessage()
    {
        _api.On(HttpMethod.Get, Search("lu"), _ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var cut = RenderPicker();

        cut.Find("#citizen-search").Input("lu");

        cut.WaitForAssertion(() => Assert.Contains("unavailable", cut.Find("#citizen-results-status").TextContent, StringComparison.OrdinalIgnoreCase));
    }

    private void SignInAs(bool officer) =>
        _api.OnJson(HttpMethod.Get, "api/me", new AccountDto(Guid.NewGuid(), "Someone", null, null, true, officer, DateTimeOffset.UtcNow));

    [Fact]
    public void NewRecordPage_ShowsThePicker_OnlyToOfficers()
    {
        SignInAs(officer: true);
        var officerPage = Render<RecordNew>();
        Assert.NotNull(officerPage.Find("#citizen-picker"));

        var other = new BunitContext();
        try
        {
            other.JSInterop.Mode = JSRuntimeMode.Loose;
            var api = new FakeApi();
            api.OnJson(HttpMethod.Get, "api/affiliations", Samples.Affiliations);
            api.OnJson(HttpMethod.Get, "api/me", new AccountDto(Guid.NewGuid(), "Someone", null, null, true, false, DateTimeOffset.UtcNow));
            api.AddTo(other.Services);

            var citizenPage = other.Render<RecordNew>();

            Assert.Empty(citizenPage.FindAll("#citizen-picker"));
        }
        finally
        {
            other.Dispose();
        }
    }

    [Fact]
    public void Officer_CreatesAServiceRecordForTheChosenCitizen()
    {
        SignInAs(officer: true);
        var luke = new DirectoryUserDto(Guid.NewGuid(), "Luke Skywalker", null);
        _api.OnJson(HttpMethod.Get, Search("lu"), new[] { luke });
        _api.OnJson(HttpMethod.Post, "api/records", Samples.Record(), HttpStatusCode.Created);
        var cut = Render<RecordNew>();

        cut.Find("#citizen-search").Input("lu");
        cut.WaitForAssertion(() => cut.Find("button.citizen-option").Click());
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains(_api.Calls, c => c.Method == HttpMethod.Post));
        using var body = JsonDocument.Parse(_api.Calls.Single(c => c.Method == HttpMethod.Post).Body!);
        Assert.Equal(luke.ObjectId, body.RootElement.GetProperty("subjectObjectId").GetGuid());
    }

    [Fact]
    public void WithoutAChosenCitizen_TheRequestHasNoSubject()
    {
        SignInAs(officer: true);
        _api.OnJson(HttpMethod.Post, "api/records", Samples.Record(), HttpStatusCode.Created);
        var cut = Render<RecordNew>();

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains(_api.Calls, c => c.Method == HttpMethod.Post));
        using var body = JsonDocument.Parse(_api.Calls.Single(c => c.Method == HttpMethod.Post).Body!);
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("subjectObjectId").ValueKind);
    }
}
