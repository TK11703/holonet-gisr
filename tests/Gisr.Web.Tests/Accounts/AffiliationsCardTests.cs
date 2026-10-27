using System.Net;
using System.Text.Json;
using Bunit;
using Gisr.Shared.Accounts;
using Gisr.Web.Accounts;
using Gisr.Web.Tests.Common;

namespace Gisr.Web.Tests.Accounts;

public class AffiliationsCardTests : BunitContext
{
    private const string Path = "api/me/affiliations";
    private readonly FakeApi _api = new();

    private static readonly AffiliationOptionGroupDto[] Options =
    [
        new("Governments & Political Organizations", ["Galactic Republic", "Galactic Empire"]),
        new("Military Organizations", ["Imperial Navy", "Stormtrooper Corps"]),
    ];

    public AffiliationsCardTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _api.OnJson(HttpMethod.Get, "api/me/affiliation-options", Options);
        _api.OnJson(HttpMethod.Get, Path, Array.Empty<AccountAffiliationDto>());
        _api.AddTo(Services);
    }

    private static AccountAffiliationDto Item(string primary, string? secondary = null, string? unit = null, string? prefix = null, string? number = null) =>
        new(Guid.NewGuid(), primary, secondary, unit, prefix, number, DateTimeOffset.UtcNow);

    [Fact]
    public void Renders_HeaderEmptyState_AndOptionalFields()
    {
        var cut = Render<AffiliationsCard>();

        Assert.Equal("Associated Affiliations", cut.Find("#affiliations-heading").TextContent.Trim());
        Assert.Equal("Associate an Affiliation", cut.Find("#associate-affiliation-heading").TextContent.Trim());
        Assert.NotNull(cut.Find("#affiliation-empty"));
        Assert.Empty(cut.FindAll("section[aria-labelledby=affiliations-heading] .card-footer"));
        Assert.Equal("true", cut.Find("#affiliation-primary").GetAttribute("aria-required"));
        Assert.Empty(cut.FindAll("#affiliation-secondary[required],#affiliation-special-unit[required],#affiliation-prefix[required],#affiliation-number[required]"));
        Assert.Equal("affiliation-form", cut.Find(".card-footer button[type=submit]").GetAttribute("form"));
    }

    [Fact]
    public void LoadFailure_ShowsAlert_AndCanBeRetried()
    {
        _api.On(HttpMethod.Get, Path, _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var cut = Render<AffiliationsCard>();
        Assert.Contains("could not be loaded", cut.Find("[role=alert]").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("form"));

        _api.OnJson(HttpMethod.Get, Path, new[] { Item("Galactic Empire") });
        cut.Find("#affiliation-retry").Click();
        cut.WaitForAssertion(() => Assert.Contains("Galactic Empire", cut.Find("#affiliation-list").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void AccountOperatingNumber_PrefillsPrefixAndNumber_AndStaysEditable()
    {
        var created = Item("Galactic Empire", null, null, "FN", "2187");
        _api.OnJson(HttpMethod.Get, "api/me", Account("TK-11703"));
        _api.OnJson(HttpMethod.Post, Path, created, HttpStatusCode.Created);
        var cut = Render<AffiliationsCard>();

        Assert.Equal("TK", cut.Find("#affiliation-prefix").GetAttribute("value"));
        Assert.Equal("11703", cut.Find("#affiliation-number").GetAttribute("value"));
        Assert.False(cut.Find("#affiliation-number").HasAttribute("readonly"));

        cut.Find("#affiliation-primary").Click();
        cut.FindAll("#affiliation-primary-menu .dropdown-item").Single(i => i.TextContent.Trim() == "Galactic Empire").Click();
        cut.Find("#affiliation-prefix").Change("FN");
        cut.Find("#affiliation-number").Change("2187");
        cut.Find("#affiliation-form").Submit();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("#affiliation-list > li")));
        using var body = JsonDocument.Parse(Assert.Single(_api.Calls, call => call.Method == HttpMethod.Post).Body!);
        Assert.Equal("FN", body.RootElement.GetProperty("prefix").GetString());
        Assert.Equal("2187", body.RootElement.GetProperty("operatingNumber").GetString());
        Assert.Equal("TK", cut.Find("#affiliation-prefix").GetAttribute("value"));
        Assert.Equal("11703", cut.Find("#affiliation-number").GetAttribute("value"));
    }

    [Theory]
    [InlineData("11703", null, "11703")]
    [InlineData(null, null, null)]
    [InlineData("not a number!", null, null)]
    public void AccountOperatingNumber_WithoutPrefixOrUnusable_PrefillsOnlyWhatFits(string? accountNumber, string? prefix, string? number)
    {
        _api.OnJson(HttpMethod.Get, "api/me", Account(accountNumber));
        var cut = Render<AffiliationsCard>();

        Assert.Equal(prefix ?? string.Empty, cut.Find("#affiliation-prefix").GetAttribute("value") ?? string.Empty);
        Assert.Equal(number ?? string.Empty, cut.Find("#affiliation-number").GetAttribute("value") ?? string.Empty);
    }

    private static AccountDto Account(string? operatingNumber) =>
        new(Guid.NewGuid(), "Leia", "leia@example.test", null, true, false, DateTimeOffset.UtcNow) { OperatingNumber = operatingNumber };

    [Fact]
    public void AssociateCard_Collapses_AndExpandsAgain_KeepingFormState()
    {
        var cut = Render<AffiliationsCard>();
        var toggle = cut.Find("#associate-affiliation-toggle");
        Assert.Equal("true", toggle.GetAttribute("aria-expanded"));
        Assert.Equal("associate-affiliation-body associate-affiliation-footer", toggle.GetAttribute("aria-controls"));
        Assert.Contains("show", cut.Find("#associate-affiliation-body").ClassList);
        Assert.Contains("show", cut.Find("#associate-affiliation-footer").ClassList);
        cut.Find("#affiliation-prefix").Change("TK");

        toggle.Click();
        Assert.Equal("false", cut.Find("#associate-affiliation-toggle").GetAttribute("aria-expanded"));
        Assert.DoesNotContain("show", cut.Find("#associate-affiliation-body").ClassList);
        Assert.Contains("collapse", cut.Find("#associate-affiliation-body").ClassList);
        Assert.DoesNotContain("show", cut.Find("#associate-affiliation-footer").ClassList);
        Assert.Contains("Associated Affiliations", cut.Find("#affiliations-heading").TextContent, StringComparison.Ordinal);

        cut.Find("#associate-affiliation-toggle").Click();
        Assert.Contains("show", cut.Find("#associate-affiliation-body").ClassList);
        Assert.Equal("TK", cut.Find("#affiliation-prefix").GetAttribute("value"));
    }

    [Fact]
    public void AddedStatus_AutoHidesAfterFiveSeconds()
    {
        _api.OnJson(HttpMethod.Post, Path, Item("Galactic Empire"), HttpStatusCode.Created);
        var cut = Render<AffiliationsCard>();
        cut.Find("#affiliation-primary").Click();
        cut.FindAll("#affiliation-primary-menu .dropdown-item").Single(i => i.TextContent.Trim() == "Galactic Empire").Click();
        cut.Find("#affiliation-form").Submit();

        cut.WaitForAssertion(() => Assert.Equal("Galactic Empire added.", cut.Find(".card-footer [role=status]").TextContent.Trim()));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".card-footer [role=status]")), TimeSpan.FromSeconds(7));
        Assert.Single(cut.FindAll("#affiliation-list > li"));
        Assert.Equal("false", cut.Find("#associate-affiliation-toggle").GetAttribute("aria-expanded"));
        Assert.DoesNotContain("show", cut.Find("#associate-affiliation-body").ClassList);
    }

    [Fact]
    public void AssociateCard_StartsCollapsed_WhenAffiliationsExist_AndExpandedWhenNone()
    {
        _api.OnJson(HttpMethod.Get, Path, new[] { Item("Galactic Empire") });
        var collapsed = Render<AffiliationsCard>();
        Assert.Equal("false", collapsed.Find("#associate-affiliation-toggle").GetAttribute("aria-expanded"));
        Assert.DoesNotContain("show", collapsed.Find("#associate-affiliation-body").ClassList);
        Assert.DoesNotContain("show", collapsed.Find("#associate-affiliation-footer").ClassList);

        _api.OnJson(HttpMethod.Get, Path, Array.Empty<AccountAffiliationDto>());
        var expanded = Render<AffiliationsCard>();
        Assert.Equal("true", expanded.Find("#associate-affiliation-toggle").GetAttribute("aria-expanded"));
    }

    [Fact]
    public void RemoveStatus_DoesNotCollapseAssociateCard()
    {
        var item = Item("Galactic Empire");
        _api.OnJson(HttpMethod.Get, Path, new[] { item });
        _api.On(HttpMethod.Delete, $"{Path}/{item.Id}", _ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var cut = Render<AffiliationsCard>();
        cut.Find("#associate-affiliation-toggle").Click();
        cut.Find("button[aria-label='Remove Galactic Empire']").Click();

        cut.WaitForAssertion(() => Assert.Contains("removed", cut.Find("section[aria-labelledby=affiliations-heading] [role=status]").TextContent, StringComparison.Ordinal));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("section[aria-labelledby=affiliations-heading] [role=status]")), TimeSpan.FromSeconds(7));
        Assert.Equal("true", cut.Find("#associate-affiliation-toggle").GetAttribute("aria-expanded"));
    }

    [Fact]
    public void ExistingAffiliations_ShowHierarchyAndPrefixedNumber()
    {
        _api.OnJson(HttpMethod.Get, Path, new[]
        {
            Item("Galactic Empire", "Stormtrooper Corps", "501st Legion", "TK", "11703"),
            Item("Black Sun"),
        });
        var cut = Render<AffiliationsCard>();

        var rows = cut.FindAll("#affiliation-list > li");
        Assert.Equal(2, rows.Count);
        Assert.Contains("Galactic Empire", rows[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("Stormtrooper Corps › 501st Legion", rows[0].TextContent, StringComparison.Ordinal);
        Assert.Equal("TK-11703", rows[0].QuerySelector(".badge")!.TextContent.Trim());
        Assert.Null(rows[1].QuerySelector(".badge"));
    }

    [Fact]
    public void Dropdown_GroupsByCategory_AndFiltersBySearch()
    {
        var cut = Render<AffiliationsCard>();
        Assert.Empty(cut.FindAll("#affiliation-primary-menu"));
        cut.Find("#affiliation-primary").Click();

        Assert.Equal("true", cut.Find("#affiliation-primary").GetAttribute("aria-expanded"));
        Assert.Equal(new[] { "Governments & Political Organizations", "Military Organizations" }, cut.FindAll("#affiliation-primary-menu .dropdown-header").Select(h => h.TextContent.Trim()));
        Assert.Equal(4, cut.FindAll("#affiliation-primary-menu .dropdown-item").Count);

        cut.Find("#affiliation-primary-search").Input("troop");
        Assert.Equal(new[] { "Stormtrooper Corps" }, cut.FindAll("#affiliation-primary-menu .dropdown-item").Select(i => i.TextContent.Trim()));
        Assert.Single(cut.FindAll("#affiliation-primary-menu .dropdown-header"));

        cut.Find("#affiliation-primary-search").Input("military");
        Assert.Equal(2, cut.FindAll("#affiliation-primary-menu .dropdown-item").Count);

        cut.Find("#affiliation-primary-search").Input("zzz");
        Assert.Empty(cut.FindAll("#affiliation-primary-menu .dropdown-item"));
        Assert.Contains("No matching affiliations", cut.Find("#affiliation-primary-menu [role=status]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void SecondaryDropdown_SharesPrimaryOptions_AndCanBeCleared()
    {
        var cut = Render<AffiliationsCard>();
        cut.Find("#affiliation-primary").Click();
        var primaryOptions = cut.FindAll("#affiliation-primary-menu .dropdown-item").Select(i => i.TextContent.Trim()).ToArray();
        cut.Find("#affiliation-primary").Click();

        Assert.Equal("None", cut.Find("#affiliation-secondary").TextContent.Trim());
        Assert.False(cut.Find("#affiliation-secondary").HasAttribute("aria-required"));
        cut.Find("#affiliation-secondary").Click();
        Assert.Equal(primaryOptions, cut.FindAll("#affiliation-secondary-menu .dropdown-item").Select(i => i.TextContent.Trim()));
        cut.FindAll("#affiliation-secondary-menu .dropdown-item").Single(i => i.TextContent.Trim() == "Imperial Navy").Click();
        Assert.Equal("Imperial Navy", cut.Find("#affiliation-secondary").TextContent.Trim());

        cut.Find("#affiliation-secondary").Click();
        cut.FindAll("#affiliation-secondary-menu .dropdown-item").Single(i => i.TextContent.Trim() == "None").Click();
        Assert.Equal("None", cut.Find("#affiliation-secondary").TextContent.Trim());
    }

    [Fact]
    public void SelectingAnOption_SetsPrimary_AndClosesMenu_AndEscapeCloses()
    {
        var cut = Render<AffiliationsCard>();
        cut.Find("#affiliation-primary").Click();
        cut.FindAll("#affiliation-primary-menu .dropdown-item").Single(i => i.TextContent.Trim() == "Galactic Empire").Click();

        Assert.Empty(cut.FindAll("#affiliation-primary-menu"));
        Assert.Equal("Galactic Empire", cut.Find("#affiliation-primary").TextContent.Trim());

        cut.Find("#affiliation-primary").Click();
        cut.Find("#affiliation-primary-search").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });
        Assert.Empty(cut.FindAll("#affiliation-primary-menu"));
    }

    [Fact]
    public void Add_SendsAllFields_AppendsRow_AndResetsForm()
    {
        var created = Item("Galactic Empire", "Stormtrooper Corps", "501st Legion", "TK", "11703");
        _api.OnJson(HttpMethod.Post, Path, created, HttpStatusCode.Created);
        var cut = Render<AffiliationsCard>();
        cut.Find("#affiliation-primary").Click();
        cut.FindAll("#affiliation-primary-menu .dropdown-item").Single(i => i.TextContent.Trim() == "Galactic Empire").Click();
        cut.Find("#affiliation-secondary").Click();
        cut.FindAll("#affiliation-secondary-menu .dropdown-item").Single(i => i.TextContent.Trim() == "Stormtrooper Corps").Click();
        cut.Find("#affiliation-special-unit").Change("501st Legion");
        cut.Find("#affiliation-prefix").Change("TK");
        cut.Find("#affiliation-number").Change("11703");
        cut.Find("#affiliation-form").Submit();

        cut.WaitForAssertion(() => Assert.Equal("TK-11703", cut.Find("#affiliation-list .badge").TextContent.Trim()));
        using var body = JsonDocument.Parse(Assert.Single(_api.Calls, call => call.Method == HttpMethod.Post).Body!);
        Assert.Equal("Galactic Empire", body.RootElement.GetProperty("primary").GetString());
        Assert.Equal("Stormtrooper Corps", body.RootElement.GetProperty("secondary").GetString());
        Assert.Equal("501st Legion", body.RootElement.GetProperty("specialUnit").GetString());
        Assert.Equal("TK", body.RootElement.GetProperty("prefix").GetString());
        Assert.Equal("11703", body.RootElement.GetProperty("operatingNumber").GetString());
        Assert.Equal("Select an affiliation", cut.Find("#affiliation-primary").TextContent.Trim());
        Assert.True(string.IsNullOrEmpty(cut.Find("#affiliation-number").GetAttribute("value")));
        Assert.Contains("added", cut.Find(".card-footer [role=status]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidationErrors_AreLinkedToFields()
    {
        _api.On(HttpMethod.Post, Path, _ => FakeApi.Problem(HttpStatusCode.BadRequest, new
        {
            status = 400,
            errors = new Dictionary<string, string[]> { ["primary"] = ["Choose a primary affiliation."], ["operatingNumber"] = ["Use 1 to 16 letters or digits."] },
        }));
        var cut = Render<AffiliationsCard>();
        cut.Find("#affiliation-form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("true", cut.Find("#affiliation-primary").GetAttribute("aria-invalid"));
            Assert.Equal("affiliation-primary-error", cut.Find("#affiliation-primary").GetAttribute("aria-describedby"));
            Assert.Contains("Choose a primary", cut.Find("#affiliation-primary-error").TextContent, StringComparison.Ordinal);
            Assert.Equal("affiliation-operatingNumber-error", cut.Find("#affiliation-number").GetAttribute("aria-describedby"));
        });
    }

    [Fact]
    public void Duplicate_ShowsConflictDetail()
    {
        _api.On(HttpMethod.Post, Path, _ => FakeApi.Problem(HttpStatusCode.Conflict, new { status = 409, title = "Conflict", detail = "This affiliation has already been added." }));
        var cut = Render<AffiliationsCard>();
        cut.Find("#affiliation-form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("already been added", cut.Find(".card-body [role=alert]").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void Remove_DeletesAndDropsRow()
    {
        var item = Item("Galactic Empire");
        _api.OnJson(HttpMethod.Get, Path, new[] { item });
        _api.On(HttpMethod.Delete, $"{Path}/{item.Id}", _ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var cut = Render<AffiliationsCard>();

        cut.Find("button[aria-label='Remove Galactic Empire']").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#affiliation-empty")));
        Assert.Single(_api.Calls, call => call.Method == HttpMethod.Delete);
    }
}
