using System.Net;
using System.Text.Json;
using Bunit;
using Gisr.Shared.Records;
using Gisr.Web.Records;
using Gisr.Web.Tests.Common;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Gisr.Web.Tests.Records;

public class RecordPagesTests : BunitContext
{
    private readonly FakeApi _api = new();

    public RecordPagesTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _api.OnJson(HttpMethod.Get, "api/me/affiliations", Samples.Affiliations);
        _api.AddTo(Services);
    }

    private string CurrentUri => Services.GetRequiredService<NavigationManager>().Uri;

    [Fact]
    public void New_StartsWithTheFirstAffiliation_AndCreatesTheRecord()
    {
        var created = Samples.Record(title: "Temple guard");
        _api.OnJson(HttpMethod.Post, "api/records", created, HttpStatusCode.Created);
        var cut = Render<RecordNew>();

        Assert.Equal("New record", cut.Find("h1").TextContent.Trim());
        Assert.Equal("Military", cut.Find("select#record-affiliation").GetAttribute("value"));
        cut.Find("#record-title").Change("Temple guard");
        cut.Find("#detail-summary").Change("Guarded the temple.");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.EndsWith($"/records/{created.Id}/edit", CurrentUri, StringComparison.Ordinal));
        var post = _api.Calls.Single(c => c.Method == HttpMethod.Post);
        using var body = JsonDocument.Parse(post.Body!);
        Assert.Equal("Temple guard", body.RootElement.GetProperty("title").GetString());
        Assert.Equal("Military", body.RootElement.GetProperty("affiliation").GetString());
        Assert.Equal("Guarded the temple.", body.RootElement.GetProperty("engagementDetail").GetProperty("summary").GetString());
    }

    [Fact]
    public void New_LoadsTheAffiliationsFromTheUsersAccount_AndLocksTheDependentTabs()
    {
        var cut = Render<RecordNew>();

        Assert.Contains(_api.Calls, c => c.PathAndQuery == "api/me/affiliations");
        Assert.DoesNotContain(_api.Calls, c => c.PathAndQuery == "api/affiliations");
        Assert.True(cut.Find("#tab-incidents").HasAttribute("disabled"));
        Assert.True(cut.Find("#tab-awards").HasAttribute("disabled"));
        Assert.True(cut.Find("#tab-artifacts").HasAttribute("disabled"));
        Assert.True(cut.Find("#delete-record").HasAttribute("disabled"));
    }

    [Fact]
    public void New_ServerValidationErrors_StayOnThePage()
    {
        _api.On(HttpMethod.Post, "api/records", _ => FakeApi.Problem(HttpStatusCode.BadRequest, new
        {
            title = "Validation",
            status = 400,
            errors = new Dictionary<string, string[]> { ["engagementDetail.summary"] = ["'Summary' is required."] },
        }));
        var cut = Render<RecordNew>();

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("'Summary' is required.", cut.Find("#detail-summary-error").TextContent, StringComparison.Ordinal));
        Assert.DoesNotContain("/records/", CurrentUri, StringComparison.Ordinal);
    }

    [Fact]
    public void Edit_PrefillsTheForm_AndSavesWithTheExpectedVersion()
    {
        var record = Samples.Record(version: 3);
        _api.OnJson(HttpMethod.Get, $"api/records/{record.Id}", record);
        _api.OnJson(HttpMethod.Put, $"api/records/{record.Id}", record with { Title = "Renamed", Version = 4 });
        var cut = Render<RecordEdit>(p => p.Add(c => c.Id, record.Id));

        Assert.Equal("Edit record", cut.Find("h1").TextContent.Trim());
        Assert.Equal("Clone Wars service", cut.Find("#record-title").GetAttribute("value"));
        Assert.Equal("Served with distinction.", cut.Find("#detail-summary").GetAttribute("value"));
        cut.Find("#record-title").Change("Renamed");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.EndsWith($"/records/{record.Id}", CurrentUri, StringComparison.Ordinal));
        var put = _api.Calls.Single(c => c.Method == HttpMethod.Put);
        using var body = JsonDocument.Parse(put.Body!);
        Assert.Equal(3, body.RootElement.GetProperty("expectedVersion").GetInt32());
        Assert.Equal("Renamed", body.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public void Edit_EnablesTheTabs()
    {
        var record = Samples.Record();
        _api.OnJson(HttpMethod.Get, $"api/records/{record.Id}", record);

        var cut = Render<RecordEdit>(p => p.Add(c => c.Id, record.Id));

        Assert.Empty(cut.FindAll("[role=tab][disabled]"));
        Assert.False(cut.Find("#delete-record").HasAttribute("disabled"));
    }

    [Fact]
    public void Edit_DeleteRecord_ConfirmsThenDeletesAndReturnsToTheList()
    {
        var record = Samples.Record();
        _api.OnJson(HttpMethod.Get, $"api/records/{record.Id}", record);
        _api.On(HttpMethod.Delete, $"api/records/{record.Id}", _ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var cut = Render<RecordEdit>(p => p.Add(c => c.Id, record.Id));

        cut.Find("#delete-record").Click();
        Assert.DoesNotContain(_api.Calls, c => c.Method == HttpMethod.Delete);
        cut.Find("#confirm-delete-record").Click();

        cut.WaitForAssertion(() => Assert.EndsWith("/records", CurrentUri, StringComparison.Ordinal));
        Assert.Single(_api.Calls, c => c.Method == HttpMethod.Delete);
    }

    [Fact]
    public void Edit_Conflict_KeepsInput_AndReloadFetchesTheLatestVersion()
    {
        var record = Samples.Record(version: 1);
        var latest = record with { Title = "Changed elsewhere", Version = 2 };
        var current = record;
        _api.On(HttpMethod.Get, $"api/records/{record.Id}", _ => FakeApi.JsonResponse(HttpStatusCode.OK, current));
        _api.On(HttpMethod.Put, $"api/records/{record.Id}", _ => FakeApi.Problem(HttpStatusCode.Conflict, new { title = "Conflict", status = 409 }));
        var cut = Render<RecordEdit>(p => p.Add(c => c.Id, record.Id));

        cut.Find("#record-title").Change("My edit");
        cut.Find("form").Submit();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#conflict-alert")));
        Assert.Equal("My edit", cut.Find("#record-title").GetAttribute("value"));

        current = latest;
        cut.Find("#conflict-alert button").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Changed elsewhere", cut.Find("#record-title").GetAttribute("value"));
            Assert.Empty(cut.FindAll("#conflict-alert"));
        });
    }

    [Fact]
    public void Edit_ShowsTheArtifactsWithRemoveControls_AndTheUploader()
    {
        var record = Samples.Record();
        var artifact = new Gisr.Shared.Artifacts.ArtifactDto(Guid.NewGuid(), record.Id, "orders.pdf", "application/pdf", 100, false, null, "Rex", DateTimeOffset.UtcNow, "pending", null);
        _api.OnJson(HttpMethod.Get, $"api/records/{record.Id}", record);
        _api.OnJson(HttpMethod.Get, $"api/records/{record.Id}/artifacts?pageSize=100",
            new Gisr.Shared.Common.PagedResult<Gisr.Shared.Artifacts.ArtifactDto>(1, 100, 1, [artifact]));

        var cut = Render<RecordEdit>(p => p.Add(c => c.Id, record.Id));

        Assert.Equal("Added artifacts", cut.Find("section#artifacts h3").TextContent.Trim());
        Assert.Contains("orders.pdf", cut.Find("section#artifacts").TextContent, StringComparison.Ordinal);
        Assert.NotNull(cut.Find($"#remove-{artifact.Id}"));
        Assert.NotNull(cut.Find("#artifact-uploader"));
    }

    [Fact]
    public void Edit_RefreshesTheArtifactList_AfterAnUpload()
    {
        var record = Samples.Record();
        var created = new Gisr.Shared.Artifacts.ArtifactDto(Guid.NewGuid(), record.Id, "orders.pdf", "application/pdf", 100, false, null, "Rex", DateTimeOffset.UtcNow, "pending", null);
        var uploaded = false;
        _api.OnJson(HttpMethod.Get, $"api/records/{record.Id}", record);
        _api.On(HttpMethod.Get, $"api/records/{record.Id}/artifacts?pageSize=100", _ => FakeApi.JsonResponse(HttpStatusCode.OK,
            new Gisr.Shared.Common.PagedResult<Gisr.Shared.Artifacts.ArtifactDto>(1, 100, uploaded ? 1 : 0, uploaded ? [created] : [])));
        _api.On(HttpMethod.Post, $"api/records/{record.Id}/artifacts", _ =>
        {
            uploaded = true;
            return FakeApi.JsonResponse(HttpStatusCode.Created, created);
        });
        var cut = Render<RecordEdit>(p => p.Add(c => c.Id, record.Id));
        Assert.Contains("No artifacts attached", cut.Find("section#artifacts").TextContent, StringComparison.Ordinal);

        cut.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>()
            .UploadFiles(InputFileContent.CreateFromBinary(new byte[10], "orders.pdf"));
        cut.Find("#upload-artifacts").Click();

        cut.WaitForAssertion(() => Assert.Contains("orders.pdf", cut.Find("section#artifacts").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void Edit_AsAnOfficer_ListsTheRecordSubjectsAffiliations()
    {
        _api.OnJson(HttpMethod.Get, "api/me", new Gisr.Shared.Accounts.AccountDto(Guid.NewGuid(), "Officer", null, null, true, true, DateTimeOffset.UtcNow));
        var record = Samples.Record(isServiceRecord: true);
        _api.OnJson(HttpMethod.Get, $"api/records/{record.Id}", record);
        _api.OnJson(HttpMethod.Get, $"api/accounts/{record.SubjectAccountId}/affiliations", new[]
        {
            new Gisr.Shared.Accounts.AccountAffiliationDto(Guid.NewGuid(), "Jedi Order", null, null, null, null, DateTimeOffset.UtcNow),
        });

        var cut = Render<RecordEdit>(p => p.Add(c => c.Id, record.Id));

        Assert.Equal(["", "Military", "Jedi Order"], cut.FindAll("select#record-affiliation option").Select(o => o.GetAttribute("value") ?? "").ToArray());
        Assert.DoesNotContain(_api.Calls, c => c.PathAndQuery == "api/me/affiliations");
    }

    [Fact]
    public void Edit_AsAnOfficer_SubjectWithoutAffiliations_KeepsTheRecordsOwnValue()
    {
        _api.OnJson(HttpMethod.Get, "api/me", new Gisr.Shared.Accounts.AccountDto(Guid.NewGuid(), "Officer", null, null, true, true, DateTimeOffset.UtcNow));
        var record = Samples.Record(isServiceRecord: true);
        _api.OnJson(HttpMethod.Get, $"api/records/{record.Id}", record);
        _api.OnJson(HttpMethod.Get, $"api/accounts/{record.SubjectAccountId}/affiliations", Array.Empty<Gisr.Shared.Accounts.AccountAffiliationDto>());

        var cut = Render<RecordEdit>(p => p.Add(c => c.Id, record.Id));

        Assert.Equal("Military", cut.Find("select#record-affiliation").GetAttribute("value"));
        Assert.False(cut.Find("#save-record").HasAttribute("disabled"));
    }

    [Fact]
    public void Edit_WhenNotAllowed_ShowsMessageInsteadOfTheForm()
    {
        var record = Samples.Record(canEdit: false);
        _api.OnJson(HttpMethod.Get, $"api/records/{record.Id}", record);

        var cut = Render<RecordEdit>(p => p.Add(c => c.Id, record.Id));

        Assert.Contains("not permitted", cut.Find("[role=alert]").TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(cut.FindAll("form"));
    }

    [Fact]
    public void Edit_UnknownRecord_ShowsNotFound()
    {
        var id = Guid.NewGuid();
        _api.On(HttpMethod.Get, $"api/records/{id}", _ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var cut = Render<RecordEdit>(p => p.Add(c => c.Id, id));

        Assert.Contains("not found", cut.Find("[role=alert]").TextContent, StringComparison.OrdinalIgnoreCase);
    }
}
