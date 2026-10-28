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
    private readonly RecordFormModel _model = RecordFormModel.New("Military");

    public CitizenPickerTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _api.OnJson(HttpMethod.Get, "api/me/affiliations", Samples.Affiliations);
        _api.AddTo(Services);
    }

    private IRenderedComponent<CitizenPicker> RenderPicker(bool forAnother = false)
    {
        _model.ForAnother = forAnother;
        return Render<CitizenPicker>(p => p.Add(c => c.Model, _model));
    }

    private static string Search(string query) => $"api/accounts?query={Uri.EscapeDataString(query)}&pageSize=10";

    [Fact]
    public void DefaultsToForMyself_WithNoSearchBox()
    {
        var cut = RenderPicker();

        Assert.True(cut.Find("#citizen-for-myself").HasAttribute("checked"));
        Assert.False(cut.Find("#citizen-for-other").HasAttribute("checked"));
        Assert.Empty(cut.FindAll("#citizen-search"));
        Assert.Empty(cut.FindAll("button.citizen-option"));
        Assert.Null(_model.SubjectObjectId);
    }

    [Fact]
    public void TheChoice_IsAnAccessibleGroupOfTwoRadios_WithoutACitizenHeading()
    {
        var cut = RenderPicker();

        Assert.Equal("Who is this record for?", cut.Find("[role=group]").GetAttribute("aria-label"));
        Assert.Equal("For myself", cut.Find("label[for=citizen-for-myself]").TextContent.Trim());
        Assert.Equal("For someone else", cut.Find("label[for=citizen-for-other]").TextContent.Trim());
        Assert.Equal(["radio", "radio"], cut.FindAll("input.btn-check").Select(i => i.GetAttribute("type")!).ToArray());
        Assert.DoesNotContain("Citizen", cut.Find("#citizen-picker").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void ChoosingForSomeoneElse_RevealsTheSearchBox_WithAnAccessibleLabel()
    {
        var cut = RenderPicker();

        cut.Find("#citizen-for-other").Change(true);

        Assert.True(_model.ForAnother);
        Assert.True(_model.NeedsCitizen);
        var input = cut.Find("input#citizen-search");
        Assert.Equal("Search the tenant directory", cut.Find($"label[for={input.Id}]").TextContent.Trim());
    }

    [Fact]
    public void ShortQueries_DoNotCallTheApi()
    {
        var cut = RenderPicker(forAnother: true);

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
        var cut = RenderPicker(forAnother: true);

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
        var cut = RenderPicker(forAnother: true);

        cut.Find("#citizen-search").Input("a&b c");

        cut.WaitForAssertion(() => Assert.Contains(_api.Calls, c => c.PathAndQuery == Search("a&b c")));
    }

    [Fact]
    public void ChoosingACitizen_ShowsThemInPlaceOfTheSearch_ChangeReopensIt_AndMyselfResets()
    {
        var luke = new DirectoryUserDto(Guid.NewGuid(), "Luke Skywalker", null);
        _api.OnJson(HttpMethod.Get, Search("lu"), new[] { luke });
        var cut = RenderPicker(forAnother: true);
        cut.Find("#citizen-search").Input("lu");
        cut.WaitForAssertion(() => cut.Find("button.citizen-option").Click());

        Assert.Equal(luke.ObjectId, _model.SubjectObjectId);
        Assert.Equal("Luke Skywalker", cut.Find("#citizen-selected").GetAttribute("value"));
        Assert.True(cut.Find("#citizen-selected").HasAttribute("readonly"));
        Assert.Empty(cut.FindAll("#citizen-search"));
        Assert.False(_model.NeedsCitizen);

        cut.Find("#citizen-change").Click();

        Assert.Null(_model.SubjectObjectId);
        Assert.True(_model.ForAnother);
        Assert.NotNull(cut.Find("#citizen-search"));

        cut.Find("#citizen-for-myself").Change(true);

        Assert.False(_model.ForAnother);
        Assert.Null(_model.SubjectObjectId);
        Assert.Empty(cut.FindAll("#citizen-search"));
        Assert.Empty(cut.FindAll("#citizen-selected"));
    }

    [Fact]
    public void SearchBox_HasThePlaceholder_AndNoHintTextBeforeSearching()
    {
        var cut = RenderPicker(forAnother: true);

        Assert.Equal("Search for the citizen by name, email, or operating number...", cut.Find("#citizen-search").GetAttribute("placeholder"));
        Assert.Empty(cut.FindAll("#citizen-results-status"));
        Assert.Empty(cut.FindAll("p.text-secondary"));
    }

    [Fact]
    public void Results_FloatOverTheForm_AndEscapeClosesThem()
    {
        _api.OnJson(HttpMethod.Get, Search("lu"), new[] { new DirectoryUserDto(Guid.NewGuid(), "Luke Skywalker", null) });
        var cut = RenderPicker(forAnother: true);

        cut.Find("#citizen-search").Input("lu");

        cut.WaitForAssertion(() => Assert.Contains("position-absolute", cut.Find("ul.list-group").ClassList));
        cut.Find("#citizen-search").KeyDown("Escape");
        Assert.Empty(cut.FindAll("button.citizen-option"));
    }

    [Fact]
    public void NoMatches_ShowsAMessage()
    {
        _api.OnJson(HttpMethod.Get, Search("zz"), Array.Empty<DirectoryUserDto>());
        var cut = RenderPicker(forAnother: true);

        cut.Find("#citizen-search").Input("zz");

        cut.WaitForAssertion(() => Assert.Contains("No matching citizens", cut.Find("#citizen-results-status").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void SearchFailure_ShowsAMessage()
    {
        _api.On(HttpMethod.Get, Search("lu"), _ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var cut = RenderPicker(forAnother: true);

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
            api.OnJson(HttpMethod.Get, "api/me/affiliations", Samples.Affiliations);
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
        _api.OnJson(HttpMethod.Get, ByObject(luke), Samples.Affiliations);
        _api.OnJson(HttpMethod.Post, "api/records", Samples.Record(), HttpStatusCode.Created);
        var cut = Render<RecordNew>();

        cut.Find("#citizen-for-other").Change(true);
        cut.Find("#citizen-search").Input("lu");
        cut.WaitForAssertion(() => cut.Find("button.citizen-option").Click());
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("#record-affiliation-loading")));
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains(_api.Calls, c => c.Method == HttpMethod.Post));
        using var body = JsonDocument.Parse(_api.Calls.Single(c => c.Method == HttpMethod.Post).Body!);
        Assert.Equal(luke.ObjectId, body.RootElement.GetProperty("subjectObjectId").GetGuid());
    }

    private static string ByObject(DirectoryUserDto user) => $"api/accounts/by-object/{user.ObjectId}/affiliations";

    private IRenderedComponent<RecordNew> ChooseLuke(DirectoryUserDto luke)
    {
        SignInAs(officer: true);
        _api.OnJson(HttpMethod.Get, Search("lu"), new[] { luke });
        var cut = Render<RecordNew>();
        cut.Find("#citizen-for-other").Change(true);
        cut.Find("#citizen-search").Input("lu");
        cut.WaitForAssertion(() => cut.Find("button.citizen-option").Click());
        return cut;
    }

    [Fact]
    public void NewRecordHeader_ForMyself_ShowsTheSignedInUser_AndForAnotherWithNobodyChosen_SaysNew()
    {
        _api.OnJson(HttpMethod.Get, "api/me", new AccountDto(Guid.NewGuid(), "Officer Cody", null, null, true, true, DateTimeOffset.UtcNow, "CC-2224"));
        var cut = Render<RecordNew>();

        Assert.Equal("Citizen Record - Officer Cody (CC-2224)", Heading(cut));

        cut.Find("#citizen-for-other").Change(true);

        cut.WaitForAssertion(() => Assert.Equal("Citizen Record - New", Heading(cut)));

        cut.Find("#citizen-for-myself").Change(true);

        cut.WaitForAssertion(() => Assert.Equal("Citizen Record - Officer Cody (CC-2224)", Heading(cut)));
    }

    [Fact]
    public void NewRecordHeader_ForSomeoneElse_ShowsTheChosenCitizen_AndTheirOperatingNumber()
    {
        var luke = new DirectoryUserDto(Guid.NewGuid(), "Luke Skywalker", null);
        _api.OnJson(HttpMethod.Get, ByObject(luke), Samples.Affiliations);
        _api.OnJson(HttpMethod.Get, $"api/accounts/by-object/{luke.ObjectId}", new CitizenDto(Guid.NewGuid(), "TK-421"));

        var cut = ChooseLuke(luke);

        cut.WaitForAssertion(() => Assert.Equal("Citizen Record - Luke Skywalker (TK-421)", Heading(cut)));
    }

    [Fact]
    public void NewRecordHeader_ForSomeoneElse_OmitsTheNumberWhenItCannotBeRead()
    {
        var luke = new DirectoryUserDto(Guid.NewGuid(), "Luke Skywalker", null);
        _api.OnJson(HttpMethod.Get, ByObject(luke), Samples.Affiliations);

        var cut = ChooseLuke(luke);

        cut.WaitForAssertion(() => Assert.Equal("Citizen Record - Luke Skywalker", Heading(cut)));
    }

    private static string Heading(IRenderedComponent<RecordNew> cut) =>
        System.Text.RegularExpressions.Regex.Replace(cut.Find("#record-card-heading").TextContent, "\\s+", " ").Trim();

    [Fact]
    public void ForSomeoneElse_WithNobodyChosenYet_BlocksSaving_AndShowsNoAffiliations()
    {
        SignInAs(officer: true);
        _api.OnJson(HttpMethod.Post, "api/records", Samples.Record(), HttpStatusCode.Created);
        var cut = Render<RecordNew>();

        cut.Find("#citizen-for-other").Change(true);

        cut.WaitForAssertion(() =>
        {
            Assert.True(cut.Find("#save-record").HasAttribute("disabled"));
            Assert.Equal([""], cut.FindAll("select#record-affiliation option").Select(o => o.GetAttribute("value") ?? "x").ToArray());
            Assert.Contains("Choose a citizen", cut.Find("#record-affiliation-help").TextContent, StringComparison.Ordinal);
        });
        cut.Find("form").Submit();
        Assert.DoesNotContain(_api.Calls, c => c.Method == HttpMethod.Post);
    }

    [Fact]
    public void ChoosingACitizen_ListsThatCitizensAffiliations_NotTheOfficers()
    {
        var luke = new DirectoryUserDto(Guid.NewGuid(), "Luke Skywalker", null);
        _api.OnJson(HttpMethod.Get, ByObject(luke), new[] { new AccountAffiliationDto(Guid.NewGuid(), "Jedi Order", null, null, null, null, DateTimeOffset.UtcNow) });

        var cut = ChooseLuke(luke);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(["", "Jedi Order"], cut.FindAll("select#record-affiliation option").Select(o => o.GetAttribute("value") ?? "").ToArray());
            Assert.Equal("Jedi Order", cut.Find("select#record-affiliation").GetAttribute("value"));
            Assert.False(cut.Find("#save-record").HasAttribute("disabled"));
        });
    }

    [Fact]
    public void ACitizenWithoutAffiliations_CannotHaveARecordSavedYet()
    {
        var luke = new DirectoryUserDto(Guid.NewGuid(), "Luke Skywalker", null);
        _api.OnJson(HttpMethod.Get, ByObject(luke), Array.Empty<AccountAffiliationDto>());

        var cut = ChooseLuke(luke);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Luke Skywalker has no affiliations yet", cut.Find("#record-affiliation-help").TextContent, StringComparison.Ordinal);
            Assert.True(cut.Find("#save-record").HasAttribute("disabled"));
            Assert.Equal([""], cut.FindAll("select#record-affiliation option").Select(o => o.GetAttribute("value") ?? "x").ToArray());
        });
    }

    [Fact]
    public void WhenTheCitizensAffiliationsFailToLoad_SavingIsBlocked()
    {
        var luke = new DirectoryUserDto(Guid.NewGuid(), "Luke Skywalker", null);
        _api.On(HttpMethod.Get, ByObject(luke), _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var cut = ChooseLuke(luke);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("could not be loaded", cut.Find("#record-affiliation-help").TextContent, StringComparison.Ordinal);
            Assert.True(cut.Find("#save-record").HasAttribute("disabled"));
        });
    }

    [Fact]
    public void ChoosingMyselfAgain_RestoresTheOfficersOwnAffiliations()
    {
        var luke = new DirectoryUserDto(Guid.NewGuid(), "Luke Skywalker", null);
        _api.OnJson(HttpMethod.Get, ByObject(luke), Array.Empty<AccountAffiliationDto>());
        var cut = ChooseLuke(luke);
        cut.WaitForAssertion(() => Assert.True(cut.Find("#save-record").HasAttribute("disabled")));

        cut.Find("#citizen-for-myself").Change(true);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Military", cut.Find("select#record-affiliation").GetAttribute("value"));
            Assert.False(cut.Find("#save-record").HasAttribute("disabled"));
        });
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
