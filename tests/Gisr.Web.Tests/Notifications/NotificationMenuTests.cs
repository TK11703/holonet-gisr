using System.Net;
using Bunit;
using Gisr.Shared.Notifications;
using Gisr.Web.Layout;
using Gisr.Web.Notifications;
using Gisr.Web.Tests.Common;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Gisr.Web.Tests.Notifications;

public class NotificationMenuTests : BunitContext
{
    private const string ListPath = "api/notifications?pageSize=20";

    private readonly FakeApi _api = new();

    public NotificationMenuTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddScoped<ShellInterop>();
        _api.AddTo(Services);
    }

    private static NotificationDto Note(string message, bool read = false, Guid? record = null) =>
        new(Guid.NewGuid(), "service_record_created", message, record, new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero), read);

    private void Serve(long unread, params NotificationDto[] items) =>
        _api.OnJson(HttpMethod.Get, ListPath, new NotificationListDto(unread, items));

    private IRenderedComponent<NotificationMenu> RenderMenu(TimeSpan? poll = null) =>
        Render<NotificationMenu>(p =>
        {
            if (poll is { } interval)
            {
                p.Add(c => c.PollInterval, interval);
            }
        });

    [Fact]
    public void Bell_HasTheAccessibleNameNotifications()
    {
        Serve(0);

        var cut = RenderMenu();

        var bell = cut.Find("button#notification-button");
        Assert.Equal("Notifications", bell.GetAttribute("aria-label"));
        Assert.Equal("menu", bell.GetAttribute("aria-haspopup"));
        Assert.Equal("false", bell.GetAttribute("aria-expanded"));
    }

    [Fact]
    public void UnreadCount_ShowsInABadge_WithAScreenReaderLabel()
    {
        Serve(3, Note("one"), Note("two"), Note("three"));

        var cut = RenderMenu();

        var badge = cut.Find("#notification-badge");
        Assert.StartsWith("3", badge.TextContent.Trim(), StringComparison.Ordinal);
        Assert.Contains("unread", cut.Find("#notification-badge .visually-hidden").TextContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NoUnread_HidesTheBadge()
    {
        Serve(0, Note("old", read: true));

        var cut = RenderMenu();

        Assert.Empty(cut.FindAll("#notification-badge"));
    }

    [Fact]
    public void LargeCounts_AreCapped()
    {
        Serve(250, Note("x"));

        var cut = RenderMenu();

        Assert.StartsWith("99+", cut.Find("#notification-badge").TextContent.Trim(), StringComparison.Ordinal);
    }

    [Fact]
    public void OpeningTheBell_ListsNotifications_UnreadOnesEmphasised()
    {
        Serve(1, Note("Officer Cody created a service record for you."), Note("An older one", read: true));
        var cut = RenderMenu();

        cut.Find("button#notification-button").Click();

        var items = cut.FindAll("#notification-menu .notification-item");
        Assert.Equal(2, items.Count);
        Assert.Contains("Officer Cody created", items[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("2026-10-02", items[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("fw-semibold", items[0].ClassName, StringComparison.Ordinal);
        Assert.DoesNotContain("fw-semibold", items[1].ClassName, StringComparison.Ordinal);
        Assert.Equal("true", cut.Find("button#notification-button").GetAttribute("aria-expanded"));
    }

    [Fact]
    public void NoNotifications_ShowsAnEmptyState()
    {
        Serve(0);
        var cut = RenderMenu();

        cut.Find("button#notification-button").Click();

        Assert.Contains("No notifications", cut.Find("#notification-menu").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("button#mark-all-read"));
    }

    [Fact]
    public void MarkAllRead_PostsAndClearsTheBadge()
    {
        Serve(2, Note("a"), Note("b"));
        _api.On(HttpMethod.Post, "api/notifications/read-all", _ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var cut = RenderMenu();
        cut.Find("button#notification-button").Click();

        cut.Find("button#mark-all-read").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("#notification-badge"));
            Assert.DoesNotContain("fw-semibold", cut.Markup, StringComparison.Ordinal);
        });
        Assert.Contains(_api.Calls, c => c.Method == HttpMethod.Post && c.PathAndQuery == "api/notifications/read-all");
    }

    [Fact]
    public void ClickingAnItem_MarksItRead_AndOpensTheRecord()
    {
        var recordId = Guid.NewGuid();
        var item = Note("A service record was created", record: recordId);
        Serve(1, item);
        _api.On(HttpMethod.Post, $"api/notifications/{item.Id}/read", _ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var cut = RenderMenu();
        cut.Find("button#notification-button").Click();

        cut.Find("#notification-menu .notification-item").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.EndsWith($"/records/{recordId}", Services.GetRequiredService<NavigationManager>().Uri, StringComparison.Ordinal);
            Assert.Empty(cut.FindAll("#notification-badge"));
        });
        Assert.Contains(_api.Calls, c => c.Method == HttpMethod.Post && c.PathAndQuery == $"api/notifications/{item.Id}/read");
    }

    [Fact]
    public void ClickingAnAlreadyReadItem_OnlyNavigates()
    {
        var recordId = Guid.NewGuid();
        Serve(0, Note("old", read: true, record: recordId));
        var cut = RenderMenu();
        cut.Find("button#notification-button").Click();

        cut.Find("#notification-menu .notification-item").Click();

        cut.WaitForAssertion(() =>
            Assert.EndsWith($"/records/{recordId}", Services.GetRequiredService<NavigationManager>().Uri, StringComparison.Ordinal));
        Assert.DoesNotContain(_api.Calls, c => c.Method == HttpMethod.Post);
    }

    [Fact]
    public void Polling_RefreshesTheBadge_OnTheInterval()
    {
        Serve(0);
        var cut = RenderMenu(TimeSpan.FromMilliseconds(30));
        Serve(4, Note("new one"));

        cut.WaitForAssertion(() => Assert.StartsWith("4", cut.Find("#notification-badge").TextContent.Trim(), StringComparison.Ordinal), TimeSpan.FromSeconds(3));
        Assert.True(_api.Calls.Count(c => c.PathAndQuery == ListPath) >= 2);
    }

    [Fact]
    public async Task Polling_StopsWhenTheComponentIsDisposed()
    {
        Serve(0);
        var cut = RenderMenu(TimeSpan.FromMilliseconds(20));
        cut.WaitForAssertion(() => Assert.True(_api.Calls.Count(c => c.PathAndQuery == ListPath) >= 3), TimeSpan.FromSeconds(15));

        await DisposeComponentsAsync();
        var afterDispose = _api.Calls.Count(c => c.PathAndQuery == ListPath);
        await Task.Delay(150);

        Assert.True(_api.Calls.Count(c => c.PathAndQuery == ListPath) <= afterDispose + 1);
    }

    [Fact]
    public void DefaultPollInterval_IsSixtySeconds()
    {
        Serve(0);

        var cut = RenderMenu();

        Assert.Equal(TimeSpan.FromSeconds(60), cut.Instance.PollInterval);
    }

    [Fact]
    public void LoadFailure_HidesTheBadge_AndTellsTheUserWhenOpened()
    {
        _api.On(HttpMethod.Get, ListPath, _ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var cut = RenderMenu();

        Assert.Empty(cut.FindAll("#notification-badge"));
        cut.Find("button#notification-button").Click();

        Assert.Contains("could not be loaded", cut.Find("#notification-menu").TextContent, StringComparison.OrdinalIgnoreCase);
    }
}
