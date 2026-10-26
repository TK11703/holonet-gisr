using Bunit;
using Gisr.Shared.Accounts;
using Gisr.Web.Accounts;
using Gisr.Web.Common;
using Gisr.Web.Layout;
using Gisr.Web.Tests.Common;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Gisr.Web.Tests.Accounts;

public class ProfileMenuTests : BunitContext
{
    private readonly FakeApi _api = new();

    public ProfileMenuTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _api.OnJson(HttpMethod.Get, "api/me", new AccountDto(
            Guid.NewGuid(), "Leia Organa", "leia@alderaan.example", null, true, false, DateTimeOffset.UtcNow));
        _api.AddTo(Services);
        Services.AddScoped<ShellInterop>();
    }

    private IRenderedComponent<ProfileMenu> RenderMenu() => Render<ProfileMenu>();

    [Fact]
    public void Trigger_ShowsUserIconAndDisplayName()
    {
        var cut = RenderMenu();

        var trigger = cut.Find("button.profile-trigger");
        var avatar = cut.Find(".avatar");
        Assert.Empty(avatar.TextContent.Trim());
        Assert.Equal("true", avatar.GetAttribute("aria-hidden"));
        var userIcon = Assert.Single(cut.FindComponents<Icon>(), icon => icon.Instance.Name == "user");
        Assert.Equal(userIcon.Find("svg").OuterHtml, avatar.QuerySelector("svg")?.OuterHtml);
        Assert.Contains("Leia Organa", trigger.TextContent, StringComparison.Ordinal);
        Assert.Equal("false", trigger.GetAttribute("aria-expanded"));
        Assert.Empty(cut.FindAll(".dropdown-menu"));
    }

    [Fact]
    public void Click_OpensRightAnchoredMenuWithAccountSettingsAndSignOut()
    {
        var cut = RenderMenu();

        cut.Find("button.profile-trigger").Click();

        Assert.Equal("true", cut.Find("button.profile-trigger").GetAttribute("aria-expanded"));
        Assert.Contains("end-0", cut.Find("#profile-menu").ClassList);
        Assert.Contains("position-relative", cut.Find(".profile-menu").ClassList);
        var settings = cut.Find("a[role=menuitem]");
        Assert.Equal("/account", settings.GetAttribute("href"));
        Assert.Contains("Account settings", settings.TextContent, StringComparison.Ordinal);

        var form = cut.Find("form[action='/auth/logout']");
        Assert.Equal("post", form.GetAttribute("method"));
        Assert.Contains("Sign out", form.QuerySelector("button[role=menuitem]")!.TextContent, StringComparison.Ordinal);
        Assert.Equal("test-antiforgery-token", form.QuerySelector("input[type=hidden]")!.GetAttribute("value"));
    }

    [Fact]
    public void Escape_ClosesMenu_AndReturnsFocusToTrigger()
    {
        var cut = RenderMenu();
        cut.Find("button.profile-trigger").Click();

        cut.Find(".profile-menu").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(cut.FindAll(".dropdown-menu"));
        Assert.Equal("false", cut.Find("button.profile-trigger").GetAttribute("aria-expanded"));
        JSInterop.VerifyFocusAsyncInvoke();
    }

    [Fact]
    public void ArrowDownOnTrigger_OpensMenu()
    {
        var cut = RenderMenu();

        cut.Find("button.profile-trigger").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        Assert.NotEmpty(cut.FindAll(".dropdown-menu"));
    }

    [Fact]
    public void BackdropClick_ClosesMenu()
    {
        var cut = RenderMenu();
        cut.Find("button.profile-trigger").Click();

        cut.Find(".menu-backdrop").Click();

        Assert.Empty(cut.FindAll(".dropdown-menu"));
    }

    [Fact]
    public void TriggerWithoutLoadedAccount_ShowsAccountFallback()
    {
        var failing = new FakeApi();
        failing.On(HttpMethod.Get, "api/me", _ => new HttpResponseMessage(System.Net.HttpStatusCode.BadGateway));
        using var other = new BunitContext();
        other.JSInterop.Mode = JSRuntimeMode.Loose;
        failing.AddTo(other.Services);
        other.Services.AddScoped<ShellInterop>();

        var cut = other.Render<ProfileMenu>();

        Assert.Contains("Account", cut.Find("button.profile-trigger").TextContent, StringComparison.Ordinal);
    }
}
