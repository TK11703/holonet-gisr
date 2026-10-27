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
        _api.OnJson(HttpMethod.Get, "api/me/affiliations", Samples.Affiliations);
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

        Assert.Empty(cut.FindAll("#filter-scope"));
        Assert.Empty(cut.FindAll("#scope-all"));
    }

    [Fact]
    public void WhenTheAccountCannotBeLoaded_TheSwitchIsHidden_AndTheListStillWorks()
    {
        _api.On(HttpMethod.Get, "api/me", _ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        _api.OnJson(HttpMethod.Get, Mine, Page(Samples.ListItem(title: "Still here")));

        var cut = Render<RecordList>();

        Assert.Empty(cut.FindAll("#filter-scope"));
        Assert.Contains("Still here", cut.Find("tbody").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Officers_SeeTheSwitch_WithMyRecordsSelectedByDefault()
    {
        SignInAs(officer: true);
        _api.OnJson(HttpMethod.Get, Mine, Page(Samples.ListItem()));

        var cut = Render<RecordList>();

        Assert.Equal(["My records", "All records"],
            cut.FindAll("#filter-scope option").Select(option => option.TextContent.Trim()).ToArray());
        Assert.Equal("mine", cut.Find("#filter-scope").GetAttribute("value"));
        Assert.Equal("Records", cut.Find("label[for='filter-scope']").TextContent.Trim());
        var toolbar = cut.Find("#filter-primary-affiliation").ParentElement!.ParentElement!;
        Assert.Equal(["filter-scope", "filter-primary-affiliation", "filter-secondary-affiliation", "sort"],
            toolbar.QuerySelectorAll("select").Select(select => select.Id ?? string.Empty).ToArray());
    }

    [Fact]
    public void ChoosingAllRecords_ReloadsWithScopeAll()
    {
        SignInAs(officer: true);
        _api.OnJson(HttpMethod.Get, Mine, Page(Samples.ListItem(title: "Mine")));
        _api.OnJson(HttpMethod.Get, All, Page(Samples.ListItem(title: "Rex record"), Samples.ListItem(title: "Cody record", subjectName: "Cody")));
        var cut = Render<RecordList>();

        cut.Find("#filter-scope").Change("all");

        cut.WaitForAssertion(() =>
        {
            var body = cut.Find("tbody").TextContent;
            Assert.Contains("Rex record", body, StringComparison.Ordinal);
            Assert.Contains("Cody record", body, StringComparison.Ordinal);
            Assert.Equal("all", cut.Find("#filter-scope").GetAttribute("value"));
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
        cut.Find("#filter-scope").Change("all");
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
        cut.Find("#filter-scope").Change("all");
        cut.WaitForAssertion(() => cut.Find("tbody tr button.subject-filter").Click());
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#subject-filter")));

        cut.Find("#filter-scope").Change("mine");

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

        cut.Find("#filter-scope").Change("all");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("No records match", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Create your first record", cut.Markup, StringComparison.Ordinal);
            Assert.NotNull(cut.Find("#filter-scope"));
        });
    }

    [Fact]
    public void OfficersWithNoPersonalRecords_CanStillChooseAllRecords()
    {
        SignInAs(officer: true);
        _api.OnJson(HttpMethod.Get, Mine, Page());
        _api.OnJson(HttpMethod.Get, All, Page(Samples.ListItem(title: "Other record")));

        var cut = Render<RecordList>();

        Assert.Contains("Create your first record", cut.Markup, StringComparison.Ordinal);
        cut.Find("#filter-scope").Change("all");

        cut.WaitForAssertion(() => Assert.Contains("Other record", cut.Find("tbody").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void RecordsAuthoredByAnotherUser_HaveNoServiceRecordBadge_InAllRecords()
    {
        SignInAs(officer: true);
        _api.OnJson(HttpMethod.Get, Mine, Page(Samples.ListItem()));
        _api.OnJson(HttpMethod.Get, All, Page(Samples.ListItem(service: true)));
        var cut = Render<RecordList>();

        cut.Find("#filter-scope").Change("all");

        cut.WaitForAssertion(() => Assert.DoesNotContain("Service record", cut.Find("tbody").TextContent, StringComparison.Ordinal));
    }
}
