using System.Net;
using Bunit;
using Gisr.Shared.Accounts;
using Gisr.Shared.Common;
using Gisr.Shared.Search;
using Gisr.Web.Search;
using Gisr.Web.Tests.Common;
using Gisr.Web.Tests.Records;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Gisr.Web.Tests.Search;

public class SearchResultsTests : BunitContext
{
    private readonly FakeApi _api = new();

    public SearchResultsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _api.OnJson(HttpMethod.Get, "api/me/affiliations", Samples.Affiliations);
        _api.AddTo(Services);
    }

    private string CurrentUri => Services.GetRequiredService<NavigationManager>().Uri;

    private static string Path(string query, int page = 1, string extra = "") =>
        $"api/search?q={Uri.EscapeDataString(query)}&page={page}&pageSize=10{extra}";

    private void SetOfficer(bool officer) =>
        _api.OnJson(HttpMethod.Get, "api/me", new AccountDto(Guid.NewGuid(), "Someone", null, null, true, officer, DateTimeOffset.UtcNow));

    private static SearchResultDto Hit(
        string title = "Temple guard",
        string snippet = "Guarded the temple at dawn",
        double score = 0.87,
        bool isSealed = false,
        string affiliation = "Military",
        string source = "record",
        string? fileName = null) =>
        new(Guid.NewGuid(), title, affiliation, "Rex", isSealed, source, source == "artifact" ? Guid.NewGuid() : null, fileName, snippet, score);

    private static PagedResult<SearchResultDto> Page(long total, params SearchResultDto[] items) => new(1, 10, total, items);

    // SupplyParameterFromQuery values can only be set by navigating, so the tests do the same.
    private IRenderedComponent<SearchResults> RenderSearch(string? query)
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(navigation.GetUriWithQueryParameter("q", query));
        return Render<SearchResults>();
    }

    [Fact]
    public void ShowsTheTitleBreadcrumb_AndTheQueryInTheBox()
    {
        _api.OnJson(HttpMethod.Get, Path("temple guard"), Page(0));

        var cut = RenderSearch("temple guard");

        Assert.Equal("Search", cut.Find("h1").TextContent.Trim());
        Assert.NotNull(cut.Find("nav[aria-label=breadcrumb]"));
        Assert.Equal("temple guard", cut.Find("input#search-query").GetAttribute("value"));
        Assert.Equal("Search", cut.Find("label[for=search-query]").TextContent.Trim());
    }

    [Fact]
    public void ListsTheResults_WithTitleLink_Affiliation_Source_Snippet_AndScore()
    {
        var record = Hit("Temple guard", "Guarded the temple at dawn", 0.87);
        var file = Hit("Fleet logs", "Hyperdrive manual text", 0.62, affiliation: "Jedi", source: "artifact", fileName: "manual.pdf");
        _api.OnJson(HttpMethod.Get, Path("temple"), Page(2, record, file));

        var cut = RenderSearch("temple");

        var items = cut.FindAll("li.search-result");
        Assert.Equal(2, items.Count);
        Assert.Equal($"/records/{record.RecordId}", items[0].QuerySelector("a.search-result-link")!.GetAttribute("href"));
        Assert.Equal("Temple guard", items[0].QuerySelector("a.search-result-link")!.TextContent.Trim());
        Assert.Contains("Military", items[0].TextContent, StringComparison.Ordinal);
        Assert.Equal("Record", items[0].QuerySelector(".search-result-source")!.TextContent.Trim());
        Assert.Contains("Guarded the temple at dawn", items[0].QuerySelector(".search-snippet")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("87%", items[0].QuerySelector(".search-score")!.TextContent, StringComparison.Ordinal);
        Assert.Equal($"/records/{file.RecordId}", items[1].QuerySelector("a.search-result-link")!.GetAttribute("href"));
        Assert.Equal("File: manual.pdf", items[1].QuerySelector(".search-result-source")!.TextContent.Trim());
        Assert.Contains("Jedi", items[1].TextContent, StringComparison.Ordinal);
        Assert.Contains("62%", items[1].QuerySelector(".search-score")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("2 results", cut.Find("#search-summary").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMatchingWordsInTheSnippet_AreHighlighted_WithoutTrustingTheText()
    {
        _api.OnJson(HttpMethod.Get, Path("temple dawn"), Page(1, Hit(snippet: "Guarded the <b>temple</b> at dawn")));

        var cut = RenderSearch("temple dawn");

        var marks = cut.FindAll(".search-snippet mark").Select(m => m.TextContent).ToArray();
        Assert.Equal(["temple", "dawn"], marks);
        Assert.Empty(cut.FindAll(".search-snippet b"));
        Assert.Contains("<b>", cut.Find(".search-snippet").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void ShortQueryWords_AreNotHighlighted()
    {
        _api.OnJson(HttpMethod.Get, Path("a temple"), Page(1, Hit(snippet: "a guard at the temple")));

        var cut = RenderSearch("a temple");

        Assert.Equal(["temple"], cut.FindAll(".search-snippet mark").Select(m => m.TextContent).ToArray());
    }

    [Fact]
    public void ASealedRecord_ShowsABadge_OnlyWhenTheApiSaysSo()
    {
        SetOfficer(true);
        _api.OnJson(HttpMethod.Get, Path("secret", extra: "&scope=all"), Page(2, Hit("Sealed one", isSealed: true), Hit("Open one")));

        var cut = RenderSearch("secret");

        var items = cut.FindAll("li.search-result");
        Assert.Equal("Sealed", items[0].QuerySelector(".badge-sealed")!.TextContent.Trim());
        Assert.Empty(items[1].QuerySelectorAll(".badge-sealed"));
    }

    [Fact]
    public void Citizens_GetNoScopeSwitch_AndSearchTheirOwnRecords()
    {
        SetOfficer(false);
        _api.OnJson(HttpMethod.Get, Path("temple"), Page(0));

        var cut = RenderSearch("temple");

        Assert.Empty(cut.FindAll("#scope-all"));
        Assert.Empty(cut.FindAll("#scope-mine"));
        Assert.DoesNotContain(_api.Calls, c => c.PathAndQuery.Contains("scope=", StringComparison.Ordinal));
    }

    [Fact]
    public void Officers_SearchAllRecordsByDefault_AndCanSwitchToTheirOwn()
    {
        SetOfficer(true);
        _api.OnJson(HttpMethod.Get, Path("temple", extra: "&scope=all"), Page(1, Hit("Everyone's")));
        _api.OnJson(HttpMethod.Get, Path("temple"), Page(1, Hit("Mine")));

        var cut = RenderSearch("temple");

        Assert.Equal("true", cut.Find("#scope-all").GetAttribute("aria-pressed"));
        Assert.Equal("Everyone's", cut.Find("a.search-result-link").TextContent.Trim());

        cut.Find("#scope-mine").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Mine", cut.Find("a.search-result-link").TextContent.Trim());
            Assert.Equal("true", cut.Find("#scope-mine").GetAttribute("aria-pressed"));
        });
    }

    [Fact]
    public void TheAffiliationFilter_IsSentWithTheSearch_AndStartsOverOnPageOne()
    {
        _api.OnJson(HttpMethod.Get, Path("temple"), Page(1, Hit("Anyone")));
        _api.OnJson(HttpMethod.Get, Path("temple", extra: "&affiliation=Jedi"), Page(1, Hit("Knight", affiliation: "Jedi")));
        var cut = RenderSearch("temple");
        Assert.Equal(["", "Military", "Jedi"], cut.FindAll("select#search-affiliation option").Select(o => o.GetAttribute("value") ?? string.Empty).ToArray());

        cut.Find("select#search-affiliation").Change("Jedi");

        cut.WaitForAssertion(() => Assert.Equal("Knight", cut.Find("a.search-result-link").TextContent.Trim()));
    }

    [Fact]
    public void Paging_ShowsThePosition_AndLoadsTheNextPage()
    {
        _api.OnJson(HttpMethod.Get, Path("temple"), Page(25, Hit("First page")));
        _api.OnJson(HttpMethod.Get, Path("temple", 2), new PagedResult<SearchResultDto>(2, 10, 25, [Hit("Second page")]));
        var cut = RenderSearch("temple");
        Assert.Contains("Page 1 of 3", cut.Find("nav[aria-label='Search pages']").TextContent, StringComparison.Ordinal);
        Assert.True(cut.Find("#previous-page").HasAttribute("disabled"));

        cut.Find("#next-page").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Second page", cut.Find("a.search-result-link").TextContent.Trim());
            Assert.Contains("Page 2 of 3", cut.Find("nav[aria-label='Search pages']").TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ASinglePage_HasNoPager()
    {
        _api.OnJson(HttpMethod.Get, Path("temple"), Page(1, Hit()));

        Assert.Empty(RenderSearch("temple").FindAll("nav[aria-label='Search pages']"));
    }

    [Fact]
    public void NoResults_SaysSoClearly()
    {
        _api.OnJson(HttpMethod.Get, Path("quantum marmalade"), Page(0));

        var cut = RenderSearch("quantum marmalade");

        var empty = cut.Find("#search-empty").TextContent;
        Assert.Contains("No results", empty, StringComparison.Ordinal);
        Assert.Contains("quantum marmalade", empty, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("li.search-result"));
    }

    [Fact]
    public void WhenSearchIsUnavailable_ThePageSaysSo_AndCanRetry()
    {
        var calls = 0;
        _api.On(HttpMethod.Get, Path("temple"), _ => Interlocked.Increment(ref calls) == 1
            ? FakeApi.Problem(HttpStatusCode.ServiceUnavailable, new { title = "Service Unavailable", status = 503 })
            : FakeApi.JsonResponse(HttpStatusCode.OK, Page(1, Hit("Recovered"))));
        var cut = RenderSearch("temple");

        Assert.Contains("temporarily unavailable", cut.Find("#search-unavailable").TextContent, StringComparison.Ordinal);
        Assert.Equal("alert", cut.Find("#search-unavailable").GetAttribute("role"));
        Assert.Empty(cut.FindAll("#search-empty"));

        cut.Find("#search-retry").Click();

        cut.WaitForAssertion(() => Assert.Equal("Recovered", cut.Find("a.search-result-link").TextContent.Trim()));
        Assert.Empty(cut.FindAll("#search-unavailable"));
    }

    [Fact]
    public void AnyOtherFailure_ShowsAGenericAlert()
    {
        _api.On(HttpMethod.Get, Path("temple"), _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var cut = RenderSearch("temple");

        Assert.Contains("could not be completed", cut.Find("[role=alert]").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#search-unavailable"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WithoutAQuery_ThePageInvitesOneToType_WithoutCallingTheApi(string? query)
    {
        var cut = RenderSearch(query);

        Assert.Contains("Enter at least 2 characters", cut.Find("#search-prompt").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain(_api.Calls, c => c.PathAndQuery.StartsWith("api/search", StringComparison.Ordinal));
    }

    [Fact]
    public void AOneCharacterQuery_IsRejectedLocally()
    {
        var cut = RenderSearch(" a ");

        Assert.Contains("at least 2 characters", cut.Find("#search-prompt").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain(_api.Calls, c => c.PathAndQuery.StartsWith("api/search", StringComparison.Ordinal));
    }

    [Fact]
    public void SubmittingTheBox_NavigatesToTheTrimmedEscapedQuery()
    {
        var cut = RenderSearch(null);

        cut.Find("input#search-query").Input("  temple & guard  ");
        cut.Find("form[role=search]").Submit();

        Assert.EndsWith("/search?q=temple%20%26%20guard", CurrentUri, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBox_LimitsTheQueryLength_AndOffersNoAutofill()
    {
        var cut = RenderSearch(null);

        var input = cut.Find("input#search-query");
        Assert.Equal("500", input.GetAttribute("maxlength"));
        Assert.Equal("off", input.GetAttribute("autocomplete"));
    }

    [Fact]
    public void ANewQuery_LoadsFreshResults_AndStartsAtPageOne()
    {
        _api.OnJson(HttpMethod.Get, Path("first"), Page(25, Hit("First result")));
        _api.OnJson(HttpMethod.Get, Path("first", 2), new PagedResult<SearchResultDto>(2, 10, 25, [Hit("First, page two")]));
        _api.OnJson(HttpMethod.Get, Path("second"), Page(1, Hit("Second result")));
        var cut = RenderSearch("first");
        cut.Find("#next-page").Click();
        cut.WaitForAssertion(() => Assert.Equal("First, page two", cut.Find("a.search-result-link").TextContent.Trim()));

        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(navigation.GetUriWithQueryParameter("q", "second"));

        cut.WaitForAssertion(() => Assert.Equal("Second result", cut.Find("a.search-result-link").TextContent.Trim()));
    }
}
