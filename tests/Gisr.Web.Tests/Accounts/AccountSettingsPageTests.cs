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
        Assert.Contains("Settings saved", cut.Find("[role=status]").TextContent, StringComparison.Ordinal);
        Assert.Equal("General Organa", Services.GetRequiredService<CurrentUserState>().Account?.DisplayName);
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
