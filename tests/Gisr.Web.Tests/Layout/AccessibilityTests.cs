using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Bunit;
using Gisr.Shared.Accounts;
using Gisr.Shared.Common;
using Gisr.Shared.Search;
using Gisr.Web.Accounts;
using Gisr.Web.Layout;
using Gisr.Web.Records;
using Gisr.Web.Search;
using Gisr.Web.Tests.Common;
using Gisr.Web.Tests.Records;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Gisr.Web.Tests.Layout;

// Rendered pages are checked against the rules in contracts/ui-contract.md that a DOM check can prove; contrast and focus visibility are checked in the CSS.
public partial class AccessibilityTests : BunitContext
{
    private readonly FakeApi _api = new();

    public AccessibilityTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddScoped<ShellInterop>();
        _api.OnJson(HttpMethod.Get, "api/affiliations", Samples.Affiliations);
        _api.OnJson(HttpMethod.Get, "api/me", new AccountDto(Guid.NewGuid(), "Leia Organa", "leia@example.test", null, true, true, DateTimeOffset.UtcNow));
        _api.OnJson(HttpMethod.Get, "api/notifications?pageSize=20", new Gisr.Shared.Notifications.NotificationListDto(0, []));
        _api.OnJson(HttpMethod.Get, "api/records?page=1&pageSize=20&sort=updatedDesc",
            new PagedResult<Gisr.Shared.Records.RecordSummaryDto>(1, 20, 2, [Samples.ListItem(title: "Alpha"), Samples.ListItem(title: "Sealed one", isSealed: true, isLocked: true)]));
        _api.OnJson(HttpMethod.Get, "api/search?q=temple&page=1&pageSize=10&scope=all",
            new PagedResult<SearchResultDto>(1, 10, 1,
                [new SearchResultDto(Guid.NewGuid(), "Temple guard", "military", "Rex", false, "record", null, null, "Guarded the temple at dawn", 0.9)]));
        _api.AddTo(Services);
    }

    private static string RepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Gisr.slnx")))
        {
            directory = directory.Parent;
        }

        return Path.Combine([directory!.FullName, .. parts]);
    }

    private static string Describe(IElement element) =>
        $"<{element.LocalName}{(element.Id is { Length: > 0 } id ? $" id=\"{id}\"" : "")}{(element.ClassName is { Length: > 0 } cls ? $" class=\"{cls}\"" : "")}>";

    private static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);

    private static bool HasAccessibleName(IElement element) =>
        HasText(element.GetAttribute("aria-label"))
        || HasText(element.GetAttribute("aria-labelledby"))
        || HasText(element.TextContent)
        || element.QuerySelectorAll("img[alt]").Any(img => HasText(img.GetAttribute("alt")));

    private static List<string> Violations<T>(IRenderedComponent<T> cut, bool expectSingleH1 = true) where T : IComponent
    {
        var all = cut.FindAll("*");
        var found = new List<string>();

        if (expectSingleH1 && cut.FindAll("h1").Count != 1)
        {
            found.Add($"expected exactly one h1 but found {cut.FindAll("h1").Count}");
        }

        foreach (var control in cut.FindAll("button, a[href], [role=button]").Where(c => !HasAccessibleName(c)))
        {
            found.Add($"{Describe(control)} has no accessible name");
        }

        var labelledFor = cut.FindAll("label[for]").Select(l => l.GetAttribute("for")).ToHashSet(StringComparer.Ordinal);
        foreach (var field in cut.FindAll("input:not([type=hidden]):not([type=submit]):not([type=button]), select, textarea"))
        {
            var named = HasText(field.GetAttribute("aria-label"))
                        || HasText(field.GetAttribute("aria-labelledby"))
                        || (HasText(field.Id) && labelledFor.Contains(field.Id))
                        || field.Closest("label") is not null;
            if (!named)
            {
                found.Add($"{Describe(field)} has no label");
            }
        }

        var ids = all.Select(e => e.Id).Where(HasText).ToList();
        found.AddRange(ids.GroupBy(i => i).Where(g => g.Count() > 1).Select(g => $"id \"{g.Key}\" is used {g.Count()} times"));

        var known = ids.ToHashSet(StringComparer.Ordinal);
        foreach (var element in all)
        {
            foreach (var attribute in new[] { "aria-describedby", "aria-labelledby" })
            {
                var targets = element.GetAttribute(attribute)?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
                found.AddRange(targets.Where(t => !known.Contains(t)).Select(t => $"{Describe(element)} {attribute} points to missing id \"{t}\""));
            }
        }

        foreach (var image in cut.FindAll("img").Where(i => i.GetAttribute("alt") is null))
        {
            found.Add($"{Describe(image)} has no alt attribute");
        }

        foreach (var home in cut.FindAll(".breadcrumb a").Where(a => a.TextContent.Trim() == "Home" && a.GetAttribute("href") != "/"))
        {
            found.Add($"{Describe(home)} Home breadcrumb does not point to the home page");
        }

        foreach (var invalid in cut.FindAll(".is-invalid").Where(i => !HasText(i.GetAttribute("aria-describedby"))))
        {
            found.Add($"{Describe(invalid)} is invalid but has no aria-describedby error message");
        }

        return found;
    }

    private static void AssertClean(IReadOnlyList<string> violations) =>
        Assert.True(violations.Count == 0, Environment.NewLine + string.Join(Environment.NewLine, violations));

    private IRenderedComponent<SearchResults> RenderSearch(string query)
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(navigation.GetUriWithQueryParameter("q", query));
        return Render<SearchResults>();
    }

    [Fact]
    public void Home_HasNamedControls_AndSemanticHeadings()
    {
        var cut = Render<Gisr.Web.Home.Home>();

        AssertClean(Violations(cut));
    }

    [Fact]
    public void Shell_HasNamedControls_AndUniqueIds()
    {
        var cut = Render<MainLayout>(p => p.Add(c => c.Body, (RenderFragment)(b => b.AddMarkupContent(0, "<h1>Page</h1>"))));

        AssertClean(Violations(cut));
    }

    [Fact]
    public void RecordList_HasNamedControls_AndLabelledFilters()
    {
        var cut = Render<RecordList>();
        cut.WaitForState(() => cut.FindAll("table, .list-group, [data-testid=records]").Count > 0 || cut.Markup.Contains("Alpha", StringComparison.Ordinal));

        AssertClean(Violations(cut));
    }

    [Fact]
    public void RecordForm_LabelsEveryField_AndDescribesEveryError()
    {
        _api.On(HttpMethod.Post, "api/records", _ => FakeApi.Problem(System.Net.HttpStatusCode.BadRequest, new
        {
            title = "Validation",
            status = 400,
            errors = new Dictionary<string, string[]> { ["title"] = ["'Title' is required."], ["engagementDetail.rank"] = ["'Rank' is required."] },
        }));
        var cut = Render<RecordNew>();
        cut.Find("form").Submit();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".is-invalid")));

        AssertClean(Violations(cut));
    }

    [Fact]
    public void AccountSettings_LabelsEveryField()
    {
        var cut = Render<AccountSettings>();
        cut.WaitForState(() => cut.FindAll("input").Count > 0);

        AssertClean(Violations(cut));
    }

    [Fact]
    public void SearchPage_NamesItsControls_AndResultLinks()
    {
        var cut = RenderSearch("temple");
        cut.WaitForState(() => cut.FindAll(".search-result").Count > 0);

        AssertClean(Violations(cut));
    }

    [Fact]
    public void EveryImage_AndIconOnlyButton_InTheMarkupSource_HasAnAccessibleName()
    {
        var problems = new List<string>();
        foreach (var file in Directory.EnumerateFiles(RepoFile("src", "Gisr.Web"), "*.razor", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                 && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            var source = File.ReadAllText(file);
            var name = Path.GetFileName(file);
            problems.AddRange(ImageWithoutAlt().Matches(source).Select(m => $"{name}: image without alt: {m.Value}"));
            problems.AddRange(IconOnlyButtonWithoutName().Matches(source).Select(m => $"{name}: icon-only button without aria-label: {m.Value}"));
            problems.AddRange(PositiveTabIndex().Matches(source).Select(m => $"{name}: positive tabindex: {m.Value}"));
        }

        Assert.True(problems.Count == 0, Environment.NewLine + string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void ThemeCss_KeepsFocusVisible_AndRemovesMotionForReducedMotionUsers()
    {
        var css = File.ReadAllText(RepoFile("src", "Gisr.Web", "wwwroot", "theme.css"));

        Assert.Contains(":focus-visible", css, StringComparison.Ordinal);
        Assert.Matches(@":focus-visible\s*\{[^}]*outline:\s*2px solid", css);
        Assert.Matches(@"@media \(prefers-reduced-motion: reduce\)\s*\{[^@]*(animation|transition)", css);
        Assert.DoesNotContain(":focus { outline: none", css, StringComparison.Ordinal);
    }

    [Fact]
    public void TheChecker_ReportsTheViolationsItIsMeantToCatch()
    {
        var cut = Render<BadPage>();

        var found = string.Join('\n', Violations(cut));

        Assert.Contains("expected exactly one h1", found, StringComparison.Ordinal);
        Assert.Contains("<button", found, StringComparison.Ordinal);
        Assert.Contains("has no label", found, StringComparison.Ordinal);
        Assert.Contains("has no alt attribute", found, StringComparison.Ordinal);
        Assert.Contains("missing id", found, StringComparison.Ordinal);
        Assert.Contains("is used 2 times", found, StringComparison.Ordinal);
        Assert.Contains("no aria-describedby", found, StringComparison.Ordinal);
    }

    private sealed class BadPage : ComponentBase
    {
        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder) =>
            builder.AddMarkupContent(0,
                "<button></button><input id=\"a\" aria-describedby=\"nope\"><img src=\"x.png\"><p id=\"d\"></p><p id=\"d\"></p><input id=\"b\" class=\"is-invalid\" aria-label=\"B\">");
    }

    [GeneratedRegex(@"<img(?![^>]*\balt=)[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ImageWithoutAlt();

    [GeneratedRegex(@"<button(?![^>]*\baria-label)[^>]*>\s*<Icon\b[^>]*/>\s*</button>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex IconOnlyButtonWithoutName();

    [GeneratedRegex(@"tabindex\s*=\s*""[1-9]", RegexOptions.IgnoreCase)]
    private static partial Regex PositiveTabIndex();
}
