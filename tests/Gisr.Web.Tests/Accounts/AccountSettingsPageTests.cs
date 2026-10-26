using System.Net;
using System.Text.Json;
using Bunit;
using Gisr.Shared.Accounts;
using Gisr.Web.Accounts;
using Gisr.Web.Common;
using Gisr.Web.Tests.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Gisr.Web.Tests.Accounts;

public class AccountSettingsPageTests : BunitContext
{
    private readonly FakeApi _api = new();
    private readonly AccountDto _account = new(
        Guid.NewGuid(), "Leia Organa", "leia@alderaan.example", "contact@alderaan.example", true, true, DateTimeOffset.UtcNow);

    public AccountSettingsPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _api.OnJson(HttpMethod.Get, "api/me", _account);
        _api.AddTo(Services);
    }

    [Fact]
    public void CardsHaveRequestedHeadersAndActionFooters()
    {
        var cut = Render<AccountSettings>();

        Assert.Equal(3, cut.FindAll(".card > .card-header h2").Count);
        Assert.Equal(2, cut.FindAll(".card > .card-footer").Count);
        Assert.Equal("Account Information", cut.Find("#identity-heading").TextContent.Trim());
        Assert.Empty(cut.FindAll("section[aria-labelledby=identity-heading] .card-footer"));
        Assert.NotNull(cut.Find("section[aria-labelledby=identity-heading] .card-body > .alert-info svg"));
        Assert.Contains("These details come from the system's access control component and cannot be altered here.", cut.Find("section[aria-labelledby=identity-heading] .alert-info").TextContent, StringComparison.Ordinal);
        Assert.Equal("Chain Code Components", cut.Find("#chain-code-heading").TextContent.Trim());
        var save = cut.Find("section[aria-labelledby=settings-heading] > .card-footer button[type=submit]");
        Assert.Equal("Save", save.TextContent.Trim());
        Assert.NotNull(save.QuerySelector("svg"));
        Assert.Equal("account-settings-form", cut.Find("section[aria-labelledby=settings-heading] > .card-footer button[type=submit]").GetAttribute("form"));
        Assert.NotNull(cut.Find("form#account-settings-form"));
        Assert.Equal("chain-code-form", cut.Find("section[aria-labelledby=chain-code-heading] > .card-footer button[type=submit]").GetAttribute("form"));
    }

    [Fact]
    public void IdentityAndPreferencesShareFirstRow_ChainCodeSpansSecondRow_OperatingNumberIsReadOnly()
    {
        _api.OnJson(HttpMethod.Get, "api/me", _account with { OperatingNumber = "TK-11703" });
        var cut = Render<AccountSettings>();
        Assert.Contains("TK-11703", cut.Find("#identity").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#identity input"));
        Assert.NotNull(cut.Find(".row > .col-lg-6:first-child #identity-heading"));
        Assert.NotNull(cut.Find(".row > .col-lg-6:nth-child(2) #settings-heading"));
        Assert.NotNull(cut.Find(".row > .col-12:nth-child(3) #chain-code-heading"));
        Assert.Equal("Not provided", cut.Find("input#chain-code-value[readonly]").GetAttribute("value"));
        Assert.Equal(2, cut.FindAll("#chain-code-form .col-lg-6 > fieldset").Count);
        Assert.Equal(4, cut.FindAll("#chain-code-form .col-md-6 textarea").Count);
    }

    [Fact]
    public void ChainCode_SaveSendsOptionalFields_AndDeleteRequiresConfirmation()
    {
        var profile = new UpdateChainCodeRequest { FullName = "Leia Organa", Homeworld = "Alderaan", EyeColor = ProfileColor.Brown };
        var dto = new ChainCodeProfileDto(profile, "00001-00002-00003-00004-00005-00006-00007-00008", DateTimeOffset.UtcNow);
        _api.OnJson(HttpMethod.Put, "api/me/chain-code", dto);
        _api.On(HttpMethod.Delete, "api/me/chain-code", _ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var cut = Render<AccountSettings>();
        cut.Find("#chain-full-name").Change("Leia Organa");
        cut.Find("#chain-homeworld").Change("Alderaan");
        cut.Find("#chain-eye-color").Change("Brown");
        cut.Find("#chain-code-form").Submit();
        cut.WaitForAssertion(() => Assert.Equal(dto.ChainCode, cut.Find("#chain-code-value").GetAttribute("value")));
        var put = Assert.Single(_api.Calls, call => call.Method == HttpMethod.Put);
        using var json = JsonDocument.Parse(put.Body!);
        Assert.Equal("Leia Organa", json.RootElement.GetProperty("fullName").GetString());
        Assert.Equal("Brown", Assert.Single(json.RootElement.GetProperty("eyeColors").EnumerateArray()).GetString());
        Assert.False(json.RootElement.TryGetProperty("operatingNumber", out _));
        cut.Find("#delete-chain-code").Click();
        Assert.DoesNotContain(_api.Calls, call => call.Method == HttpMethod.Delete);
        cut.Find("#cancel-delete-chain-code").Click();
        Assert.Empty(cut.FindAll("#confirm-delete-chain-code"));
        cut.Find("#delete-chain-code").Click();
        cut.Find("#confirm-delete-chain-code").Click();
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("#delete-chain-code")));
        Assert.Single(_api.Calls, call => call.Method == HttpMethod.Delete);
        Assert.True(string.IsNullOrEmpty(cut.Find("#chain-full-name").GetAttribute("value")));
    }

    [Fact]
    public void ChainCode_ColorsSupportDistinctRemovableTags_AndReloadLegacySelections()
    {
        _api.OnJson(HttpMethod.Get, "api/me/chain-code", new ChainCodeProfileDto(
            new UpdateChainCodeRequest { EyeColor = ProfileColor.Brown }, "12345", DateTimeOffset.UtcNow));
        _api.OnJson(HttpMethod.Put, "api/me/chain-code", new ChainCodeProfileDto(
            new UpdateChainCodeRequest(), "67890", DateTimeOffset.UtcNow));
        var cut = Render<AccountSettings>();

        Assert.NotNull(cut.Find("button[aria-label='Remove Brown eye color']"));
        cut.Find("#chain-eye-color").Change("Blue");
        cut.Find("#chain-eye-color").Change("Green");
        cut.Find("#chain-eye-color").Change("Blue");
        Assert.Single(cut.FindAll("button[aria-label='Remove Blue eye color']"));
        cut.Find("button[aria-label='Remove Brown eye color']").Click();
        Assert.Empty(cut.FindAll("button[aria-label='Remove Brown eye color']"));
        cut.Find("#chain-code-form").Submit();

        using var json = JsonDocument.Parse(Assert.Single(_api.Calls, call => call.Method == HttpMethod.Put).Body!);
        Assert.Equal(new[] { "Blue", "Green" }, json.RootElement.GetProperty("eyeColors").EnumerateArray().Select(color => color.GetString()));
    }

    [Fact]
    public void ChainCode_ReloadsAllSavedFields_AndLinksValidationErrors()
    {
        var profile = new UpdateChainCodeRequest { Homeworld = "Naboo", Biography = "Senator", SkinColor = ProfileColor.Pale };
        _api.OnJson(HttpMethod.Get, "api/me/chain-code", new ChainCodeProfileDto(profile, "12345", DateTimeOffset.UtcNow));
        _api.On(HttpMethod.Put, "api/me/chain-code", _ => FakeApi.Problem(HttpStatusCode.BadRequest, new
        {
            status = 400, errors = new Dictionary<string, string[]> { ["homeworld"] = ["Too long."] },
        }));
        var cut = Render<AccountSettings>();
        Assert.Equal("Naboo", cut.Find("#chain-homeworld").GetAttribute("value"));
        Assert.Contains("Senator", cut.Find("#chain-biography").TextContent, StringComparison.Ordinal);
        cut.Find("#chain-code-form").Submit();
        cut.WaitForAssertion(() =>
        {
            Assert.Equal("true", cut.Find("#chain-homeworld").GetAttribute("aria-invalid"));
            Assert.Equal("chain-homeworld-error", cut.Find("#chain-homeworld").GetAttribute("aria-describedby"));
            Assert.Contains("Too long", cut.Find("#chain-homeworld-error").TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void AccountLabelsAndFieldOrder_KeepIdentityValuesAndBadge()
    {
        var cut = Render<AccountSettings>();

        Assert.Equal(new[] { "User Name", "Operating Number", "Officer credential" }, cut.FindAll("#identity dt").Select(term => term.TextContent.Trim()));
        Assert.Equal("leia@alderaan.example", cut.Find("#identity dd").TextContent.Trim());
        Assert.Equal("Held", cut.Find("#identity dd .badge.text-bg-success").TextContent.Trim());
        Assert.Equal("Notification Account", cut.Find("label[for=contact-email]").TextContent.Trim());
        Assert.Equal("email", cut.Find("#contact-email").GetAttribute("type"));
        Assert.False(cut.Find("#contact-email").HasAttribute("required"));
    }

    [Fact]
    public void OfficerCredential_AbsentUsesNeutralTextBadge()
    {
        _api.OnJson(HttpMethod.Get, "api/me", _account with { IsOfficer = false });
        var cut = Render<AccountSettings>();

        Assert.Equal("Not held", cut.Find("#identity .badge.text-bg-secondary").TextContent.Trim());
    }

    [Fact]
    public void RendersTitleAndBreadcrumb()
    {
        var cut = Render<AccountSettings>();

        Assert.Equal("Account settings", cut.Find("h1").TextContent.Trim());
        Assert.NotNull(cut.Find("nav[aria-label=breadcrumb]"));
    }

    [Fact]
    public void ShowsReadOnlyIdentity_AndOfficerCredential()
    {
        var cut = Render<AccountSettings>();

        var identity = cut.Find("#identity").TextContent;
        Assert.Contains("leia@alderaan.example", identity, StringComparison.Ordinal);
        Assert.Contains("Officer credential", identity, StringComparison.Ordinal);
        Assert.Contains("Held", identity, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#identity input"));
    }

    [Fact]
    public void PrefillsEditableFields()
    {
        var cut = Render<AccountSettings>();

        Assert.Equal("Leia Organa", cut.Find("input#display-name").GetAttribute("value"));
        Assert.Equal("contact@alderaan.example", cut.Find("input#contact-email").GetAttribute("value"));
        Assert.True(cut.Find("input#notify").HasAttribute("checked"));
    }

    [Fact]
    public void Save_SendsChanges_ShowsStatus_AndUpdatesSharedState()
    {
        var updated = _account with { DisplayName = "General Organa", NotifyOnServiceRecord = false };
        _api.OnJson(HttpMethod.Put, "api/me/settings", updated);
        var cut = Render<AccountSettings>();

        cut.Find("input#display-name").Change("General Organa");
        cut.Find("input#notify").Change(false);
        cut.Find("form").Submit();

        var put = _api.Calls.Single(c => c.Method == HttpMethod.Put);
        using var body = JsonDocument.Parse(put.Body!);
        Assert.Equal("General Organa", body.RootElement.GetProperty("displayName").GetString());
        Assert.False(body.RootElement.GetProperty("notifyOnServiceRecord").GetBoolean());
        var status = cut.Find("section[aria-labelledby=settings-heading] .card-footer > [aria-live=polite] .alert-success[role=status]");
        Assert.Equal("Preferences saved...", status.TextContent.Trim());
        Assert.NotNull(status.QuerySelector("svg[aria-hidden=true]"));
        Assert.NotNull(cut.Find("section[aria-labelledby=settings-heading] .card-footer > button ~ [aria-live=polite] .alert-success"));
        Assert.Empty(cut.FindAll("section[aria-labelledby=settings-heading] .card-body .alert-success"));
        Assert.Equal("General Organa", Services.GetRequiredService<CurrentUserState>().Account?.DisplayName);
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("section[aria-labelledby=settings-heading] .alert-success")), TimeSpan.FromSeconds(7));
    }

    [Fact]
    public void FailedSave_ClearsPreviousPreferencesSuccess()
    {
        _api.OnJson(HttpMethod.Put, "api/me/settings", _account);
        var cut = Render<AccountSettings>();
        cut.Find("#account-settings-form").Submit();
        Assert.NotNull(cut.Find("section[aria-labelledby=settings-heading] .card-footer .alert-success"));

        _api.On(HttpMethod.Put, "api/me/settings", _ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        cut.Find("#account-settings-form").Submit();
        Assert.Empty(cut.FindAll("section[aria-labelledby=settings-heading] [role=status]"));
        Assert.Contains("could not be saved", cut.Find("section[aria-labelledby=settings-heading] .card-body [role=alert]").TextContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidationFailure_ShowsInlineErrorsLinkedToFields()
    {
        _api.On(HttpMethod.Put, "api/me/settings", _ => FakeApi.Problem(HttpStatusCode.BadRequest, new
        {
            title = "Validation",
            status = 400,
            errors = new Dictionary<string, string[]> { ["displayName"] = ["'Display Name' must not be empty."], ["contactEmail"] = ["Not a valid email address."] },
        }));
        var cut = Render<AccountSettings>();

        cut.Find("input#display-name").Change("");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            var input = cut.Find("input#display-name");
            Assert.Equal("true", input.GetAttribute("aria-invalid"));
            Assert.Equal("display-name-error", input.GetAttribute("aria-describedby"));
            Assert.Contains("must not be empty", cut.Find("#display-name-error").TextContent, StringComparison.Ordinal);
            Assert.Contains("Not a valid email", cut.Find("#contact-email-error").TextContent, StringComparison.Ordinal);
            Assert.NotNull(cut.Find("[role=alert]"));
            Assert.Empty(cut.FindAll("[role=status]"));
        });
    }

    [Fact]
    public void UnexpectedFailure_ShowsGenericError()
    {
        _api.On(HttpMethod.Put, "api/me/settings", _ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var cut = Render<AccountSettings>();

        cut.Find("form").Submit();

        Assert.Contains("could not be saved", cut.Find("[role=alert]").TextContent, StringComparison.OrdinalIgnoreCase);
    }
}
