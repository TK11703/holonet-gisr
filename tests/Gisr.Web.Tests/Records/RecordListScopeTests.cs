using System.Net;
using Bunit;
using Gisr.Shared.Accounts;
using Gisr.Shared.Common;
using Gisr.Shared.Records;
using Gisr.Web.Records;
using Gisr.Web.Tests.Common;

namespace Gisr.Web.Tests.Records;

public class RecordListScopeTests : BunitContext
{
    private const string Mine = "api/records?page=1&pageSize=20&sort=updatedDesc";
    private const string All = Mine + "&scope=all";

    private readonly FakeApi _api = new();

    public RecordListScopeTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _api.OnJson(HttpMethod.Get, "api/affiliations", Samples.Affiliations);
        _api.AddTo(Services);
    }

    private void SignInAs(bool officer) =>
        _api.OnJson(HttpMethod.Get, "api/me", new AccountDto(Guid.NewGuid(), "Someone", null, null, true, officer, DateTimeOffset.UtcNow));

    private static PagedResult<RecordSummaryDto> Page(params RecordSummaryDto[] items) => new(1, 20, items.Length, items);

    [Fact]
    public void NonOfficers_DoNotSeeTheScopeSwitch()
    {
        SignInAs(officer: false);
        _api.OnJson(HttpMethod.Get, Mine, Page(Samples.ListItem()));

        var cut = Render<RecordList>();

        Assert.Empty(cut.FindAll("#scope-mine"));
        Assert.Empty(cut.FindAll("#scope-all"));
    }

    [Fact]
    public void WhenTheAccountCannotBeLoaded_TheSwitchIsHidden_AndTheListStillWorks()
    {
        _api.On(HttpMethod.Get, "api/me", _ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        _api.OnJson(HttpMethod.Get, Mine, Page(Samples.ListItem(title: "Still here")));

        var cut = Render<RecordList>();

        Assert.Empty(cut.FindAll("#scope-all"));
        Assert.Contains("Still here", cut.Find("tbody").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Officers_SeeTheSwitch_WithMyRecordsSelectedByDefault()
    {
        SignInAs(officer: true);
        _api.OnJson(HttpMethod.Get, Mine, Page(Samples.ListItem()));

        var cut = Render<RecordList>();

        Assert.Equal("My records", cut.Find("#scope-mine").TextContent.Trim());
        Assert.Equal("All records", cut.Find("#scope-all").TextContent.Trim());
        Assert.Equal("true", cut.Find("#scope-mine").GetAttribute("aria-pressed"));
        Assert.Equal("false", cut.Find("#scope-all").GetAttribute("aria-pressed"));
        Assert.NotNull(cut.Find("[role=group][aria-label='Record scope']"));
    }

    [Fact]
    public void ChoosingAllRecords_ReloadsWithScopeAll()
    {
        SignInAs(officer: true);
        _api.OnJson(HttpMethod.Get, Mine, Page(Samples.ListItem(title: "Mine")));
        _api.OnJson(HttpMethod.Get, All, Page(Samples.ListItem(title: "Rex record"), Samples.ListItem(title: "Cody record", subjectName: "Cody")));
        var cut = Render<RecordList>();

        cut.Find("#scope-all").Click();

        cut.WaitForAssertion(() =>
        {
            var body = cut.Find("tbody").TextContent;
            Assert.Contains("Rex record", body, StringComparison.Ordinal);
            Assert.Contains("Cody record", body, StringComparison.Ordinal);
            Assert.Equal("true", cut.Find("#scope-all").GetAttribute("aria-pressed"));
        });
        Assert.Contains(_api.Calls, c => c.PathAndQuery == All);
    }

    [Fact]
    public void ClickingASubject_FiltersAllRecordsBySubject_AndCanBeCleared()
    {
        var rex = Guid.NewGuid();
        SignInAs(officer: true);
        _api.OnJson(HttpMethod.Get, Mine, Page(Samples.ListItem(title: "Mine")));
        _api.OnJson(HttpMethod.Get, All, Page(
            Samples.ListItem(title: "Rex record", subjectId: rex),
            Samples.ListItem(title: "Cody record", subjectName: "Cody")));
        _api.OnJson(HttpMethod.Get, $"{All}&subjectAccountId={rex}", Page(Samples.ListItem(title: "Rex record", subjectId: rex)));
        var cut = Render<RecordList>();
        cut.Find("#scope-all").Click();
        cut.WaitForAssertion(() => Assert.Contains("Cody record", cut.Find("tbody").TextContent, StringComparison.Ordinal));

        cut.Find("tbody tr button.subject-filter").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("Cody record", cut.Find("tbody").TextContent, StringComparison.Ordinal);
            Assert.Contains("Subject: Rex", cut.Find("#subject-filter").TextContent, StringComparison.Ordinal);
        });
        cut.Find("#clear-subject").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Cody record", cut.Find("tbody").TextContent, StringComparison.Ordinal);
            Assert.Empty(cut.FindAll("#subject-filter"));
        });
    }

    [Fact]
    public void SubjectsAreNotFilterable_InMyRecords()
    {
        SignInAs(officer: true);
        _api.OnJson(HttpMethod.Get, Mine, Page(Samples.ListItem()));

        var cut = Render<RecordList>();

        Assert.Empty(cut.FindAll("button.subject-filter"));
    }

    [Fact]
    public void SwitchingBackToMyRecords_ClearsTheSubjectFilter()
    {
        var rex = Guid.NewGuid();
        SignInAs(officer: true);
        _api.OnJson(HttpMethod.Get, Mine, Page(Samples.ListItem(title: "Mine")));
        _api.OnJson(HttpMethod.Get, All, Page(Samples.ListItem(title: "Rex record", subjectId: rex)));
        _api.OnJson(HttpMethod.Get, $"{All}&subjectAccountId={rex}", Page(Samples.ListItem(title: "Rex record", subjectId: rex)));
        var cut = Render<RecordList>();
        cut.Find("#scope-all").Click();
        cut.WaitForAssertion(() => cut.Find("tbody tr button.subject-filter").Click());
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#subject-filter")));

        cut.Find("#scope-mine").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Mine", cut.Find("tbody").TextContent, StringComparison.Ordinal);
            Assert.Empty(cut.FindAll("#subject-filter"));
        });
    }

    [Fact]
    public void AllRecordsWithNothingInIt_SaysNoRecordsMatch_NotTheFirstRecordInvitation()
    {
        SignInAs(officer: true);
        _api.OnJson(HttpMethod.Get, Mine, Page(Samples.ListItem()));
        _api.OnJson(HttpMethod.Get, All, Page());
        var cut = Render<RecordList>();

        cut.Find("#scope-all").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("No records match", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Create your first record", cut.Markup, StringComparison.Ordinal);
            Assert.NotNull(cut.Find("#scope-mine"));
        });
    }

    [Fact]
    public void ServiceRecords_AreBadged_InAllRecords()
    {
        SignInAs(officer: true);
        _api.OnJson(HttpMethod.Get, Mine, Page(Samples.ListItem()));
        _api.OnJson(HttpMethod.Get, All, Page(Samples.ListItem(service: true)));
        var cut = Render<RecordList>();

        cut.Find("#scope-all").Click();

        cut.WaitForAssertion(() => Assert.Contains("Service record", cut.Find("tbody").TextContent, StringComparison.Ordinal));
    }
}
