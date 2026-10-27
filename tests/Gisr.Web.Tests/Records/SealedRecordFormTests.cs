using System.Net;
using System.Text.Json;
using Bunit;
using Gisr.Shared.Accounts;
using Gisr.Shared.Common;
using Gisr.Shared.Records;
using Gisr.Web.Common;
using Gisr.Web.Records;
using Gisr.Web.Tests.Common;
using Microsoft.AspNetCore.Components;

namespace Gisr.Web.Tests.Records;

public class SealedRecordFormTests : BunitContext
{
    private RecordFormModel _model = RecordFormModel.New("Military");
    private Func<RecordFormModel, Task> _save = _ => Task.CompletedTask;
    private int _saves;
    private readonly FakeApi _api = new();

    public SealedRecordFormTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _api.OnJson(HttpMethod.Get, "api/me/affiliations", Samples.Affiliations);
        _api.AddTo(Services);
        _model.Detail["summary"] = "x";
        _model.Detail["branch"] = "Navy";
        _model.Detail["rank"] = "Captain";
    }

    private IRenderedComponent<RecordForm> RenderForm(bool allowSealing = true) =>
        Render<RecordForm>(p => p
            .Add(c => c.Model, _model)
            .Add(c => c.Affiliations, Samples.Affiliations)
            .Add(c => c.AllowSealing, allowSealing)
            .Add(c => c.OnSave, (Func<RecordFormModel, Task>)(async m =>
            {
                _saves++;
                await _save(m);
            }))
            .Add(c => c.OnReload, EventCallback.Factory.Create(this, () => { })));

    private static RecordFormModel ExistingSealed() =>
        RecordFormModel.FromDto(Samples.Record(isSealed: true, isServiceRecord: true, version: 4));

    private static ApiException Problem(int status, params (string Field, string Message)[] errors) =>
        new(status, "Problem", null,
            errors.ToDictionary(e => e.Field, e => new[] { e.Message }, StringComparer.Ordinal),
            new Dictionary<string, JsonElement>(), null);

    [Fact]
    public void NewRecordForSelf_HasNoSealSwitch()
    {
        var cut = RenderForm();

        Assert.Empty(cut.FindAll("#seal-record"));
    }

    [Fact]
    public void NewRecordForAnotherCitizen_ShowsTheSwitch_OnlyWhenSealingIsAllowed()
    {
        _model.SubjectObjectId = Guid.NewGuid();

        Assert.NotNull(RenderForm(true).Find("input#seal-record"));
        Assert.Empty(RenderForm(false).FindAll("#seal-record"));
    }

    [Fact]
    public void TheSwitch_IsAnAccessibleSwitchWithALabel()
    {
        _model.SubjectObjectId = Guid.NewGuid();
        var cut = RenderForm();

        var toggle = cut.Find("input#seal-record");
        Assert.Equal("switch", toggle.GetAttribute("role"));
        Assert.Equal("Seal this record", cut.Find($"label[for={toggle.Id}]").TextContent.Trim());
    }

    [Fact]
    public void TurningTheSwitchOn_RevealsARequiredMaskedCodeField_WithGuidance()
    {
        _model.SubjectObjectId = Guid.NewGuid();
        var cut = RenderForm();
        Assert.Empty(cut.FindAll("#override-code"));

        cut.Find("input#seal-record").Change(true);

        var input = cut.Find("input#override-code");
        Assert.Equal("password", input.GetAttribute("type"));
        Assert.Equal("new-password", input.GetAttribute("autocomplete"));
        Assert.Equal("true", input.GetAttribute("aria-required"));
        Assert.Equal("Override code", cut.Find($"label[for={input.Id}]").TextContent.Replace("*", string.Empty).Trim());
        Assert.Contains("shown to no one afterwards", cut.Find("#override-code-help").TextContent, StringComparison.Ordinal);
        Assert.Contains("out of band", cut.Find("#override-code-help").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void SavingWithoutACode_IsBlocked_WithAnInlineError()
    {
        _model.SubjectObjectId = Guid.NewGuid();
        var cut = RenderForm();
        cut.Find("input#seal-record").Change(true);

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            var input = cut.Find("#override-code");
            Assert.Equal("true", input.GetAttribute("aria-invalid"));
            Assert.Equal("override-code-error", input.GetAttribute("aria-describedby"));
            Assert.Contains("8 to 64", cut.Find("#override-code-error").TextContent, StringComparison.Ordinal);
        });
        Assert.Equal(0, _saves);
    }

    [Theory]
    [InlineData("1234567")]
    [InlineData("")]
    public void CodesShorterThan8_AreBlocked(string code)
    {
        _model.SubjectObjectId = Guid.NewGuid();
        var cut = RenderForm();
        cut.Find("input#seal-record").Change(true);
        cut.Find("#override-code").Input(code);

        cut.Find("form").Submit();

        Assert.Equal(0, _saves);
    }

    [Fact]
    public void CodesLongerThan64_AreBlocked()
    {
        _model.SubjectObjectId = Guid.NewGuid();
        var cut = RenderForm();
        cut.Find("input#seal-record").Change(true);
        cut.Find("#override-code").Input(new string('x', 65));

        cut.Find("form").Submit();

        Assert.Equal(0, _saves);
        Assert.NotNull(cut.Find("#override-code-error"));
    }

    [Fact]
    public void AValidCode_IsSentWithTheSealFlag()
    {
        _model.SubjectObjectId = Guid.NewGuid();
        var cut = RenderForm();
        cut.Find("input#seal-record").Change(true);
        cut.Find("#override-code").Input("open sesame 42");

        cut.Find("form").Submit();

        var request = _model.ToCreateRequest();
        Assert.Equal(1, _saves);
        Assert.True(request.IsSealed);
        Assert.Equal("open sesame 42", request.OverrideCode);
        Assert.Equal(_model.SubjectObjectId, request.SubjectObjectId);
    }

    [Fact]
    public void WithoutSealing_NoCodeIsSent_EvenIfOneWasTypedBefore()
    {
        _model.SubjectObjectId = Guid.NewGuid();
        var cut = RenderForm();
        cut.Find("input#seal-record").Change(true);
        cut.Find("#override-code").Input("typed then abandoned");
        cut.Find("input#seal-record").Change(false);

        var request = _model.ToCreateRequest();

        Assert.False(request.IsSealed);
        Assert.Null(request.OverrideCode);
        Assert.Empty(cut.FindAll("#override-code"));
    }

    [Fact]
    public void ServerCodeErrors_AreShownInline()
    {
        _model.SubjectObjectId = Guid.NewGuid();
        _save = _ => throw Problem(400, ("overrideCode", "An override code is required to seal a record."), ("isSealed", "Only a service record can be sealed."));
        var cut = RenderForm();
        cut.Find("input#seal-record").Change(true);
        cut.Find("#override-code").Input("open sesame 42");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
            Assert.Contains("is required to seal", cut.Find("#override-code-error").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void EditingASealedRecord_ShowsTheCodeIsSet_WithChangeCode_AndNoRemoveAction()
    {
        _model = ExistingSealed();
        var cut = RenderForm();

        Assert.True(cut.Find("input#seal-record").HasAttribute("checked"));
        Assert.Contains("An override code is set", cut.Find("#override-code-set").TextContent, StringComparison.Ordinal);
        Assert.Equal("Change code", cut.Find("button#change-code").TextContent.Trim());
        Assert.Empty(cut.FindAll("input#override-code"));
        Assert.DoesNotContain(cut.FindAll("#seal-section button"), b => b.TextContent.Contains("Remove", StringComparison.OrdinalIgnoreCase)
                                                                        || b.TextContent.Contains("Clear", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SavingAnEditWithoutChangingTheCode_SendsNoCode_AndKeepsItSealed()
    {
        _model = ExistingSealed();
        var cut = RenderForm();

        cut.Find("form").Submit();

        var request = _model.ToUpdateRequest();
        Assert.Equal(1, _saves);
        Assert.True(request.IsSealed);
        Assert.Null(request.OverrideCode);
    }

    [Fact]
    public void ChangeCode_RevealsTheField_AndRequiresAValidNewCode()
    {
        _model = ExistingSealed();
        var cut = RenderForm();
        cut.Find("button#change-code").Click();

        cut.Find("form").Submit();
        Assert.Equal(0, _saves);

        cut.Find("#override-code").Input("a brand new code");
        cut.Find("form").Submit();

        var request = _model.ToUpdateRequest();
        Assert.Equal(1, _saves);
        Assert.True(request.IsSealed);
        Assert.Equal("a brand new code", request.OverrideCode);
    }

    [Fact]
    public void TurningTheSwitchOff_Unseals_AndNeverSendsACode()
    {
        _model = ExistingSealed();
        var cut = RenderForm();

        cut.Find("input#seal-record").Change(false);
        cut.Find("form").Submit();

        var request = _model.ToUpdateRequest();
        Assert.Equal(1, _saves);
        Assert.False(request.IsSealed);
        Assert.Null(request.OverrideCode);
    }

    [Fact]
    public void SealingAnExistingServiceRecord_NeedsANewCode()
    {
        _model = RecordFormModel.FromDto(Samples.Record(isSealed: false, isServiceRecord: true, version: 2));
        var cut = RenderForm();

        cut.Find("input#seal-record").Change(true);
        Assert.NotNull(cut.Find("input#override-code"));
        cut.Find("form").Submit();

        Assert.Equal(0, _saves);
    }

    [Fact]
    public void ARecordYouAuthoredAboutYourself_NeverOffersSealing()
    {
        _model = RecordFormModel.FromDto(Samples.Record(isServiceRecord: false, version: 2));
        var cut = RenderForm(allowSealing: false);

        Assert.Empty(cut.FindAll("#seal-record"));
    }

    [Fact]
    public void FromDto_CarriesTheSealedState_ButNeverACode()
    {
        var model = ExistingSealed();

        Assert.True(model.IsSealed);
        Assert.True(model.HasOverrideCode);
        Assert.Null(model.OverrideCode);
        Assert.False(model.ChangingCode);
    }

    [Fact]
    public void ChoosingMyself_InThePicker_TurnsSealingOff()
    {
        _model.SubjectObjectId = Guid.NewGuid();
        _model.SubjectDisplayName = "Luke";
        _model.IsSealed = true;
        _model.OverrideCode = "open sesame 42";
        var cut = Render<CitizenPicker>(p => p.Add(c => c.Model, _model));

        cut.Find("#citizen-myself").Click();

        Assert.Null(_model.SubjectObjectId);
        Assert.False(_model.IsSealed);
        Assert.Null(_model.OverrideCode);
    }

    [Fact]
    public void NewRecordPage_OfficerCanSealARecordForTheChosenCitizen()
    {
        var luke = new DirectoryUserDto(Guid.NewGuid(), "Luke Skywalker", null);
        _api.OnJson(HttpMethod.Get, "api/me", new AccountDto(Guid.NewGuid(), "Officer", null, null, true, true, DateTimeOffset.UtcNow));
        _api.OnJson(HttpMethod.Get, "api/accounts?query=lu&pageSize=10", new[] { luke });
        _api.OnJson(HttpMethod.Get, $"api/accounts/by-object/{luke.ObjectId}/affiliations", Samples.Affiliations);
        _api.OnJson(HttpMethod.Post, "api/records", Samples.Record(), HttpStatusCode.Created);
        var cut = Render<RecordNew>();
        Assert.Empty(cut.FindAll("#seal-record"));

        cut.Find("#citizen-search").Input("lu");
        cut.WaitForAssertion(() => cut.Find("button.citizen-option").Click());
        cut.Find("input#seal-record").Change(true);
        cut.Find("#override-code").Input("open sesame 42");
        cut.Find("#record-title").Change("Commendation");
        cut.Find("#detail-summary").Change("Details");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains(_api.Calls, c => c.Method == HttpMethod.Post));
        using var body = JsonDocument.Parse(_api.Calls.Single(c => c.Method == HttpMethod.Post).Body!);
        Assert.Equal(luke.ObjectId, body.RootElement.GetProperty("subjectObjectId").GetGuid());
        Assert.True(body.RootElement.GetProperty("isSealed").GetBoolean());
        Assert.Equal("open sesame 42", body.RootElement.GetProperty("overrideCode").GetString());
    }

    [Fact]
    public void EditPage_OfficerOfAServiceRecord_SeesTheSealControls_ButNotForAnOwnRecord()
    {
        _api.OnJson(HttpMethod.Get, "api/me", new AccountDto(Guid.NewGuid(), "Officer", null, null, true, true, DateTimeOffset.UtcNow));
        var service = Samples.Record(isSealed: true, isServiceRecord: true);
        var own = Samples.Record(isServiceRecord: false);
        _api.OnJson(HttpMethod.Get, $"api/records/{service.Id}", service);
        _api.OnJson(HttpMethod.Get, $"api/records/{own.Id}", own);

        var serviceCut = Render<RecordEdit>(p => p.Add(c => c.Id, service.Id));
        var ownCut = Render<RecordEdit>(p => p.Add(c => c.Id, own.Id));

        Assert.NotNull(serviceCut.Find("input#seal-record"));
        Assert.Empty(ownCut.FindAll("#seal-record"));
    }
}

public class SealedRecordListTests : BunitContext
{
    private const string Default = "api/records?page=1&pageSize=20&sort=updatedDesc";

    private readonly FakeApi _api = new();

    public SealedRecordListTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _api.OnJson(HttpMethod.Get, "api/me/affiliations", Samples.Affiliations);
        _api.AddTo(Services);
    }

    private void Serve(params RecordSummaryDto[] items) =>
        _api.OnJson(HttpMethod.Get, Default, new PagedResult<RecordSummaryDto>(1, 20, items.Length, items));

    [Fact]
    public void ALockedRecord_IsARedactedRow_WithALockIconTheSealedLabelAndTheCreatedDate()
    {
        var locked = Samples.LockedItem();
        Serve(locked, Samples.ListItem(title: "Ordinary record"));

        var cut = Render<RecordList>();

        var row = cut.FindAll("tbody tr").Single(r => r.QuerySelector("a[href='/records/" + locked.Id + "']") is not null);
        Assert.Contains("Sealed record", row.TextContent, StringComparison.Ordinal);
        Assert.Contains("Created 2026-10-01", row.TextContent, StringComparison.Ordinal);
        Assert.NotNull(row.QuerySelector("svg"));
        Assert.DoesNotContain("Rex", row.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("military", row.TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("2019", row.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLockedRow_LinksToTheRecordPage_WhereTheAccessDeniedStateLives()
    {
        var locked = Samples.LockedItem();
        Serve(locked);

        var cut = Render<RecordList>();

        Assert.Equal("Sealed record", cut.Find($"a[href='/records/{locked.Id}']").TextContent.Trim());
    }

    [Fact]
    public void ALockedRecord_CountsAsHavingRecords_SoTheFirstRecordInvitationStaysHidden()
    {
        Serve(Samples.LockedItem());

        var cut = Render<RecordList>();

        Assert.DoesNotContain("Create your first record", cut.Markup, StringComparison.Ordinal);
        Assert.NotNull(cut.Find("table"));
    }

    [Fact]
    public void ASealedRecordThatIsNotLocked_ShowsItsContentWithASealedBadge()
    {
        Serve(Samples.ListItem(title: "Officer view", isSealed: true, service: true));

        var cut = Render<RecordList>();

        var row = cut.Find("tbody tr").TextContent;
        Assert.Contains("Officer view", row, StringComparison.Ordinal);
        Assert.Contains("Sealed", row, StringComparison.Ordinal);
        Assert.Contains("Service record", row, StringComparison.Ordinal);
    }
}
