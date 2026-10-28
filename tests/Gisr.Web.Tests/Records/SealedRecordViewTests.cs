using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Bunit;
using Gisr.Shared.Artifacts;
using Gisr.Shared.Records;
using Gisr.Web.Layout;
using Gisr.Web.Records;
using Gisr.Web.Tests.Common;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Gisr.Web.Tests.Records;

public class SealedRecordViewTests : BunitContext
{
    private const string UserOid = "33333333-3333-3333-3333-333333333333";
    private const string Token = "view-token-view-token-view-token-view-token-1234";

    private readonly FakeApi _api = new();
    private readonly RecordDto _record;

    public SealedRecordViewTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _api.OnJson(HttpMethod.Get, "api/me/affiliations", Samples.Affiliations);
        _api.AddTo(Services);
        Services.AddScoped<ShellInterop>();
        _record = Samples.Record(title: "Secret commendation", isSealed: true, canEdit: false);
        ServeLocked();
        ServeArtifacts();
    }

    private string ArtifactsPath => $"api/records/{_record.Id}/artifacts?pageSize=100";

    private void ServeArtifacts(params ArtifactDto[] items) =>
        _api.OnJson(HttpMethod.Get, ArtifactsPath, new Gisr.Shared.Common.PagedResult<ArtifactDto>(1, 100, items.Length, items));

    private string RecordPath => $"api/records/{_record.Id}";

    private string OverridePath => $"{RecordPath}/override";

    private string StatusPath => $"{RecordPath}/override/status";

    private ViewGrantStore Store => Services.GetRequiredService<ViewGrantStore>();

    private void ServeLocked() =>
        _api.On(HttpMethod.Get, RecordPath, _ => FakeApi.Problem(HttpStatusCode.Forbidden, new Dictionary<string, object>
        {
            ["title"] = "Forbidden",
            ["status"] = 403,
            ["detail"] = "This record is sealed.",
            ["sealed"] = true,
        }));

    private void ServeUnlock() =>
        _api.OnJson(HttpMethod.Post, OverridePath, new UnlockedRecordDto(_record, Token, DateTimeOffset.UtcNow.AddMinutes(15)));

    private IRenderedComponent<RecordView> RenderView(TimeSpan? poll = null) =>
        Render<RecordView>(p =>
        {
            p.Add(c => c.Id, _record.Id);
            p.AddCascadingValue(Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity([new Claim("oid", UserOid)], "test")))));
            if (poll is { } interval)
            {
                p.Add(c => c.PollInterval, interval);
            }
        });

    private static void EnterCode(IRenderedComponent<RecordView> cut, string code)
    {
        cut.Find("input#override-code").Input(code);
        cut.Find("form#override-form").Submit();
    }

    [Fact]
    public void SealedAndLocked_ShowsAccessDenied_WithoutAnyRecordContent()
    {
        var cut = RenderView();

        Assert.Contains("Access denied. This record is sealed.", cut.Find("[role=alert]").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Secret commendation", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#history"));
        Assert.Empty(cut.FindAll("a#edit-record"));
        Assert.DoesNotContain(_api.Calls, c => c.PathAndQuery.Contains("history", StringComparison.Ordinal));
    }

    [Fact]
    public void OverrideForm_IsMaskedLabelledAndNotAutofilled()
    {
        var cut = RenderView();

        var input = cut.Find("input#override-code");
        Assert.Equal("password", input.GetAttribute("type"));
        Assert.Equal("off", input.GetAttribute("autocomplete"));
        Assert.Equal("Override code", cut.Find($"label[for={input.Id}]").TextContent.Trim());
        Assert.Equal("Unlock", cut.Find("form#override-form button[type=submit]").TextContent.Trim());
    }

    [Fact]
    public void EmptyCode_IsRejectedLocally_WithoutCallingTheApi()
    {
        var cut = RenderView();

        cut.Find("form#override-form").Submit();

        Assert.Contains("Enter the override code", cut.Find("#override-error").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain(_api.Calls, c => c.Method == HttpMethod.Post);
    }

    [Fact]
    public void WrongCode_ShowsTheAttemptsRemaining_AndClearsTheField()
    {
        _api.On(HttpMethod.Post, OverridePath, _ => FakeApi.Problem(HttpStatusCode.Forbidden, new Dictionary<string, object>
        {
            ["title"] = "Forbidden",
            ["status"] = 403,
            ["attemptsRemaining"] = 3,
        }));
        var cut = RenderView();

        EnterCode(cut, "wrong code");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Incorrect override code. 3 attempts remaining.", cut.Find("#override-error").TextContent, StringComparison.Ordinal);
            Assert.Equal(string.Empty, cut.Find("input#override-code").GetAttribute("value") ?? string.Empty);
            Assert.Contains("Access denied", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void LastRemainingAttempt_UsesTheSingularWording()
    {
        _api.On(HttpMethod.Post, OverridePath, _ => FakeApi.Problem(HttpStatusCode.Forbidden, new Dictionary<string, object> { ["status"] = 403, ["attemptsRemaining"] = 1 }));
        var cut = RenderView();

        EnterCode(cut, "wrong code");

        cut.WaitForAssertion(() =>
            Assert.Contains("1 attempt remaining", cut.Find("#override-error").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void TooManyAttempts_ShowsTheRetryMessage_AndDisablesTheForm()
    {
        _api.On(HttpMethod.Post, OverridePath, _ =>
        {
            var response = FakeApi.Problem(HttpStatusCode.TooManyRequests, new Dictionary<string, object> { ["title"] = "Too Many Requests", ["status"] = 429 });
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(540));
            return response;
        });
        var cut = RenderView();

        EnterCode(cut, "any code");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Too many attempts. Try again in 9 minutes.", cut.Find("#override-error").TextContent, StringComparison.Ordinal);
            Assert.True(cut.Find("input#override-code").HasAttribute("disabled"));
            Assert.True(cut.Find("form#override-form button[type=submit]").HasAttribute("disabled"));
        });
    }

    [Fact]
    public void CorrectCode_ShowsTheRecord_WithTheOverrideBanner_AndNoHistoryOrEdit()
    {
        ServeUnlock();
        var cut = RenderView();

        EnterCode(cut, "open sesame 42");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Secret commendation", cut.Find("nav[aria-label=breadcrumb] li.active").TextContent.Trim());
            Assert.Equal("Viewing under override. Access ends when you leave this page.", cut.Find("#override-banner > div").TextContent.Trim());
            Assert.Empty(cut.FindAll("#history"));
            Assert.Empty(cut.FindAll("a#edit-record"));
            Assert.Empty(cut.FindAll("form#override-form"));
            Assert.Contains("Geonosis", cut.Find("#incidents").TextContent, StringComparison.Ordinal);
        });
        using var body = JsonDocument.Parse(_api.Calls.Single(c => c.Method == HttpMethod.Post).Body!);
        Assert.Equal("open sesame 42", body.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain(_api.Calls, c => c.PathAndQuery.Contains("history", StringComparison.Ordinal));
    }

    [Fact]
    public void Unseal_AsksForConfirmation_ThenPostsTheViewTokenAndShowsTheOrdinaryRecord()
    {
        ServeUnlock();
        var unsealed = _record with { IsSealed = false, Version = 3, CanEdit = false };
        _api.On(HttpMethod.Post, $"{RecordPath}/unseal", _ =>
        {
            _api.On(HttpMethod.Get, RecordPath, _ => FakeApi.JsonResponse(HttpStatusCode.OK, unsealed));
            _api.OnJson(HttpMethod.Get, $"{RecordPath}/history?page=1&pageSize=20", new Gisr.Shared.Common.PagedResult<RevisionDto>(1, 20, 0, []));
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var cut = RenderView();
        EnterCode(cut, "open sesame 42");
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#unseal-record")));

        cut.Find("#unseal-record").Click();
        Assert.DoesNotContain(_api.Calls, c => c.PathAndQuery.EndsWith("/unseal", StringComparison.Ordinal));
        cut.Find("#confirm-unseal").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("#override-banner"));
            Assert.Equal("Secret commendation", cut.Find("nav[aria-label=breadcrumb] li.active").TextContent.Trim());
        });
        var call = _api.Calls.Single(c => c.PathAndQuery.EndsWith("/unseal", StringComparison.Ordinal));
        Assert.Equal(HttpMethod.Post, call.Method);
        Assert.Equal(Token, call.Headers["X-View-Token"]);
        Assert.DoesNotContain("Sealed", cut.Find("#record-header").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Unseal_CanBeCancelled()
    {
        ServeUnlock();
        var cut = RenderView();
        EnterCode(cut, "open sesame 42");
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#unseal-record")));

        cut.Find("#unseal-record").Click();
        cut.Find("#cancel-unseal").Click();

        Assert.Empty(cut.FindAll("#unseal-confirmation"));
        Assert.NotNull(cut.Find("#unseal-record"));
        Assert.DoesNotContain(_api.Calls, c => c.PathAndQuery.EndsWith("/unseal", StringComparison.Ordinal));
    }

    [Fact]
    public void Unseal_Failure_KeepsTheViewing_AndShowsAnError()
    {
        ServeUnlock();
        _api.On(HttpMethod.Post, $"{RecordPath}/unseal", _ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var cut = RenderView();
        EnterCode(cut, "open sesame 42");
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#unseal-record")));

        cut.Find("#unseal-record").Click();
        cut.Find("#confirm-unseal").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("could not be unsealed", cut.Find("#unseal-failed").TextContent, StringComparison.Ordinal);
            Assert.NotNull(cut.Find("#override-banner"));
        });
    }

    [Fact]
    public void EditIsUnavailableDuringAViewing_EvenIfTheRecordSaysCanEdit()
    {
        _api.OnJson(HttpMethod.Post, OverridePath, new UnlockedRecordDto(_record with { CanEdit = true }, Token, DateTimeOffset.UtcNow.AddMinutes(15)));
        var cut = RenderView();

        EnterCode(cut, "open sesame 42");

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#override-banner")));
        Assert.Empty(cut.FindAll("#record-actions-button"));
    }

    [Fact]
    public void TheViewingIsRegisteredInTheServerSideStore_NeverRenderedToTheBrowser()
    {
        ServeUnlock();
        var cut = RenderView();

        EnterCode(cut, "open sesame 42");

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#override-banner")));
        Assert.DoesNotContain(Token, cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("open sesame 42", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("viewing-id", cut.Markup, StringComparison.Ordinal);
        var viewingId = Guid.Parse(cut.Find("[data-viewing-id]").GetAttribute("data-viewing-id")!);
        Assert.True(Store.TryGet(UserOid, viewingId, out var entry));
        Assert.Equal(_record.Id, entry.RecordId);
        Assert.Equal(Token, entry.Token);
    }

    [Fact]
    public async Task Disposing_RevokesOnlyItsOwnGrant_AndRemovesOnlyItsOwnEntry()
    {
        ServeUnlock();
        _api.On(HttpMethod.Delete, OverridePath, _ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var other = Store.Register(UserOid, Guid.NewGuid(), "someone-elses-token");
        var cut = RenderView();
        EnterCode(cut, "open sesame 42");
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#override-banner")));
        var viewingId = Guid.Parse(cut.Find("[data-viewing-id]").GetAttribute("data-viewing-id")!);

        await DisposeComponentsAsync();

        var delete = Assert.Single(_api.Calls, c => c.Method == HttpMethod.Delete);
        Assert.Equal(OverridePath, delete.PathAndQuery);
        Assert.Equal(Token, delete.Headers["X-View-Token"]);
        Assert.False(Store.TryGet(UserOid, viewingId, out _));
        Assert.True(Store.TryGet(UserOid, other, out _));
    }

    [Fact]
    public async Task Disposing_WithoutAViewing_MakesNoRevokeCall()
    {
        var cut = RenderView();
        Assert.NotNull(cut.Find("#override-form"));

        await DisposeComponentsAsync();

        Assert.DoesNotContain(_api.Calls, c => c.Method == HttpMethod.Delete);
    }

    [Fact]
    public async Task Disposing_StillCleansUp_WhenTheRevokeCallFails()
    {
        ServeUnlock();
        _api.On(HttpMethod.Delete, OverridePath, _ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var cut = RenderView();
        EnterCode(cut, "open sesame 42");
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#override-banner")));
        var viewingId = Guid.Parse(cut.Find("[data-viewing-id]").GetAttribute("data-viewing-id")!);

        await DisposeComponentsAsync();

        Assert.False(Store.TryGet(UserOid, viewingId, out _));
    }

    [Fact]
    public void WhenTheStatusPollReports404_TheRecordIsClearedAndAccessDeniedReturns()
    {
        ServeUnlock();
        var polls = 0;
        _api.On(HttpMethod.Get, StatusPath, _ => Interlocked.Increment(ref polls) < 2
            ? new HttpResponseMessage(HttpStatusCode.NoContent)
            : FakeApi.Problem(HttpStatusCode.NotFound, new Dictionary<string, object> { ["status"] = 404 }));
        var cut = RenderView(TimeSpan.FromMilliseconds(30));
        EnterCode(cut, "open sesame 42");
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#override-banner")));
        var viewingId = Guid.Parse(cut.Find("[data-viewing-id]").GetAttribute("data-viewing-id")!);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Access denied. This record is sealed.", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Secret commendation", cut.Markup, StringComparison.Ordinal);
            Assert.NotNull(cut.Find("form#override-form"));
        }, TimeSpan.FromSeconds(3));

        Assert.False(Store.TryGet(UserOid, viewingId, out _));
        var poll = _api.Calls.First(c => c.PathAndQuery == StatusPath);
        Assert.Equal(Token, poll.Headers["X-View-Token"]);
    }

    [Fact]
    public void TransientPollFailures_DoNotEndTheViewing()
    {
        ServeUnlock();
        _api.On(HttpMethod.Get, StatusPath, _ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var cut = RenderView(TimeSpan.FromMilliseconds(30));
        EnterCode(cut, "open sesame 42");
        Assert.True(SpinWait.SpinUntil(() => _api.Calls.Count(c => c.PathAndQuery == StatusPath) >= 2, TimeSpan.FromSeconds(15)));

        Assert.NotNull(cut.Find("#override-banner"));
    }

    [Fact]
    public void DuringAViewing_TheArtifactsAreListedWithViewingUrls_AndTheTokenOnlyGoesToTheApi()
    {
        var image = new ArtifactDto(Guid.NewGuid(), _record.Id, "secret.png", "image/png", 100, true, "A secret map", "Officer", DateTimeOffset.UtcNow, "skipped", "unsupported_type");
        ServeArtifacts(image);
        ServeUnlock();
        var cut = RenderView();
        Assert.DoesNotContain(_api.Calls, c => c.PathAndQuery == ArtifactsPath);

        EnterCode(cut, "open sesame 42");

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("section#artifacts img")));
        var viewingId = cut.Find("[data-viewing-id]").GetAttribute("data-viewing-id");
        Assert.Equal($"/media/artifacts/{image.Id}/thumbnail?v={viewingId}", cut.Find("section#artifacts img").GetAttribute("src"));
        Assert.Equal(Token, _api.Calls.Single(c => c.PathAndQuery == ArtifactsPath).Headers["X-View-Token"]);
        Assert.DoesNotContain(Token, cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("button.artifact-remove"));
    }

    [Fact]
    public void WhenTheArtifactsReturnNotFound_TheViewingEnds_AndTheAccessDeniedStateReturns()
    {
        ServeUnlock();
        _api.On(HttpMethod.Get, ArtifactsPath, _ => FakeApi.Problem(HttpStatusCode.NotFound, new Dictionary<string, object> { ["status"] = 404 }));
        var cut = RenderView();

        EnterCode(cut, "open sesame 42");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Access denied. This record is sealed.", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Secret commendation", cut.Markup, StringComparison.Ordinal);
            Assert.NotNull(cut.Find("form#override-form"));
            Assert.Empty(cut.FindAll("#override-banner"));
        });
    }

    [Fact]
    public void ForOfficers_TheArtifactsAreRequestedWithoutAViewToken()
    {
        _api.OnJson(HttpMethod.Get, RecordPath, _record with { CanEdit = true });
        _api.OnJson(HttpMethod.Get, $"{RecordPath}/history?page=1&pageSize=20", new Gisr.Shared.Common.PagedResult<RevisionDto>(1, 20, 0, []));

        RenderView();

        var call = Assert.Single(_api.Calls, c => c.PathAndQuery == ArtifactsPath);
        Assert.DoesNotContain("X-View-Token", call.Headers.Keys);
    }

    [Fact]
    public void ThePollInterval_DefaultsToTwentySeconds()
    {
        var cut = RenderView();

        Assert.Equal(TimeSpan.FromSeconds(20), cut.Instance.PollInterval);
    }

    [Fact]
    public async Task PollingStops_WhenTheComponentIsDisposed()
    {
        ServeUnlock();
        _api.On(HttpMethod.Get, StatusPath, _ => new HttpResponseMessage(HttpStatusCode.NoContent));
        _api.On(HttpMethod.Delete, OverridePath, _ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var cut = RenderView(TimeSpan.FromMilliseconds(20));
        EnterCode(cut, "open sesame 42");
        Assert.True(SpinWait.SpinUntil(() => _api.Calls.Count(c => c.PathAndQuery == StatusPath) >= 2, TimeSpan.FromSeconds(3)));

        await DisposeComponentsAsync();
        var afterDispose = _api.Calls.Count(c => c.PathAndQuery == StatusPath);
        await Task.Delay(150);

        Assert.True(_api.Calls.Count(c => c.PathAndQuery == StatusPath) <= afterDispose + 1);
    }

    [Fact]
    public async Task RevisitingAfterAViewing_AsksForTheCodeAgain()
    {
        ServeUnlock();
        var first = RenderView();
        EnterCode(first, "open sesame 42");
        first.WaitForAssertion(() => Assert.NotNull(first.Find("#override-banner")));
        _api.On(HttpMethod.Delete, OverridePath, _ => new HttpResponseMessage(HttpStatusCode.NoContent));
        await DisposeComponentsAsync();

        var second = RenderView();

        Assert.Contains("Access denied", second.Markup, StringComparison.Ordinal);
        Assert.NotNull(second.Find("form#override-form"));
    }

    [Fact]
    public void ForOfficers_ASealedRecordShowsInFullWithASealedBadge()
    {
        _api.OnJson(HttpMethod.Get, RecordPath, _record with { CanEdit = true });
        _api.OnJson(HttpMethod.Get, $"{RecordPath}/history?page=1&pageSize=20", new Gisr.Shared.Common.PagedResult<RevisionDto>(1, 20, 0, []));
        var cut = RenderView();

        Assert.Equal("Secret commendation", cut.Find("nav[aria-label=breadcrumb] li.active").TextContent.Trim());
        var padlock = cut.Find("#record-header #sealed-indicator");
        Assert.Equal("Sealed", padlock.QuerySelector("svg[role=img]")!.GetAttribute("aria-label"));
        Assert.False(string.IsNullOrWhiteSpace(padlock.GetAttribute("title")));
        Assert.NotNull(cut.Find("#record-actions-button"));
        Assert.NotNull(cut.Find("#history"));
        Assert.Empty(cut.FindAll("#override-banner"));
        Assert.Empty(cut.FindAll("form#override-form"));
    }

    [Fact]
    public void ANonSealed403_IsNotTreatedAsASealedRecord()
    {
        _api.On(HttpMethod.Get, RecordPath, _ => FakeApi.Problem(HttpStatusCode.Forbidden, new Dictionary<string, object> { ["status"] = 403 }));

        var cut = RenderView();

        Assert.Empty(cut.FindAll("form#override-form"));
        Assert.Contains("could not be loaded", cut.Find("[role=alert]").TextContent, StringComparison.OrdinalIgnoreCase);
    }
}
