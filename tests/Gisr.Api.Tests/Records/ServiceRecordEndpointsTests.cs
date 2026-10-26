using System.Net;
using System.Net.Http.Json;
using Gisr.Api.Tests.Common;
using Gisr.Shared.Accounts;
using Gisr.Shared.Common;
using Gisr.Shared.Notifications;
using Gisr.Shared.Records;

namespace Gisr.Api.Tests.Records;

[Collection("api")]
public class ServiceRecordEndpointsTests(PostgresFixture postgres)
{
    private static CreateRecordRequest ServiceRecord(Guid? subjectObjectId, string title = "Commendation") =>
        new(title, "military", null, null,
            new Dictionary<string, string> { ["summary"] = "Confidential service", ["branch"] = "Navy", ["rank"] = "Admiral" },
            SubjectObjectId: subjectObjectId);

    // A citizen who has already signed in, so an account row exists.
    private static async Task<(HttpClient Client, Guid Oid, Guid AccountId)> SignedInAsync(ApiFactory factory, string name = "Citizen")
    {
        var oid = Guid.NewGuid();
        var client = factory.CreateUserClient(oid, name: name);
        var account = (await (await client.GetAsync("/api/me")).Content.ReadFromJsonAsync<AccountDto>())!;
        return (client, oid, account.Id);
    }

    [Fact]
    public async Task NonOfficer_CreatingForAnotherCitizen_Returns403_AndCreatesNothing()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var target = await SignedInAsync(factory, "Target");

        var response = await factory.CreateUserClient().PostAsJsonAsync("/api/records", ServiceRecord(target.Oid));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, await factory.ScalarAsync<long>("SELECT count(*) FROM record"));
    }

    [Fact]
    public async Task NonOfficer_CannotUseTheTenantDirectory_ToCreateAccounts()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var stranger = factory.Directory.Add("Tenant Stranger");

        var response = await factory.CreateUserClient().PostAsJsonAsync("/api/records", ServiceRecord(stranger));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, factory.Directory.Calls);
        Assert.Equal(0, await factory.ScalarAsync<long>($"SELECT count(*) FROM account WHERE entra_object_id = '{stranger}'"));
    }

    [Fact]
    public async Task Officer_CreatesAServiceRecord_ForACitizenWhoAlreadyHasAnAccount_WithoutAskingTheDirectory()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var citizen = await SignedInAsync(factory, "Citizen");
        var officer = factory.CreateUserClient(officer: true, name: "Officer");
        var officerAccount = (await (await officer.GetAsync("/api/me")).Content.ReadFromJsonAsync<AccountDto>())!;

        var response = await officer.PostAsJsonAsync("/api/records", ServiceRecord(citizen.Oid));
        var record = await response.Content.ReadFromJsonAsync<RecordDto>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(citizen.AccountId, record!.SubjectAccountId);
        Assert.Equal(officerAccount.Id, record.AuthorAccountId);
        Assert.Equal("Citizen", record.SubjectDisplayName);
        Assert.Equal("Officer", record.AuthorDisplayName);
        Assert.True(record.IsServiceRecord);
        Assert.True(record.CanEdit);
        Assert.Equal($"/api/records/{record.Id}", response.Headers.Location?.ToString());
        Assert.Equal(0, factory.Directory.Calls);
    }

    [Fact]
    public async Task Officer_CreatesAServiceRecord_ForATenantUserWhoNeverSignedIn_ViaAPlaceholderAccount()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var oid = factory.Directory.Add("Padme Amidala", "padme@naboo.example");
        var officer = factory.CreateUserClient(officer: true, name: "Officer");

        var response = await officer.PostAsJsonAsync("/api/records", ServiceRecord(oid));
        var record = await response.Content.ReadFromJsonAsync<RecordDto>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Padme Amidala", record!.SubjectDisplayName);
        Assert.True(record.IsServiceRecord);
        Assert.Equal(1, await factory.ScalarAsync<long>(
            $"SELECT count(*) FROM account WHERE entra_object_id = '{oid}' AND tenant_id = '{ApiFactory.TestTenant}' AND last_sign_in_at IS NULL"));
        Assert.Equal("padme@naboo.example", await factory.ScalarAsync<string>($"SELECT sign_in_email FROM account WHERE entra_object_id = '{oid}'"));
    }

    [Fact]
    public async Task WhenThatPersonSignsInLater_TheyOwnTheSameAccount_SeeTheRecordAndTheNotification()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var oid = factory.Directory.Add("Padme Amidala");
        var created = (await (await factory.CreateUserClient(officer: true, name: "Officer")
            .PostAsJsonAsync("/api/records", ServiceRecord(oid, "Senate honours"))).Content.ReadFromJsonAsync<RecordDto>())!;

        var padme = factory.CreateUserClient(oid, name: "Padmé from token");
        var me = (await (await padme.GetAsync("/api/me")).Content.ReadFromJsonAsync<AccountDto>())!;
        var page = (await (await padme.GetAsync("/api/records")).Content.ReadFromJsonAsync<PagedResult<RecordSummaryDto>>())!;
        var notifications = (await (await padme.GetAsync("/api/notifications")).Content.ReadFromJsonAsync<NotificationListDto>())!;

        Assert.Equal(created.SubjectAccountId, me.Id);
        Assert.Equal("Padme Amidala", me.DisplayName);
        Assert.Equal(["Senate honours"], page.Items.Select(r => r.Title));
        Assert.Equal(1, notifications.UnreadCount);
        Assert.Equal(1, await factory.ScalarAsync<long>("SELECT count(*) FROM account WHERE last_sign_in_at IS NOT NULL AND entra_object_id = '" + oid + "'"));
    }

    [Fact]
    public async Task SecondRecordForTheSamePerson_ReusesThePlaceholder()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var oid = factory.Directory.Add("Padme Amidala");
        var officer = factory.CreateUserClient(officer: true);

        await officer.PostAsJsonAsync("/api/records", ServiceRecord(oid, "One"));
        await officer.PostAsJsonAsync("/api/records", ServiceRecord(oid, "Two"));

        Assert.Equal(1, await factory.ScalarAsync<long>($"SELECT count(*) FROM account WHERE entra_object_id = '{oid}'"));
        Assert.Equal(2, await factory.ScalarAsync<long>("SELECT count(*) FROM record"));
    }

    [Fact]
    public async Task Officer_PickingSomeoneNotInTheTenantDirectory_Returns404_AndCreatesNothing()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);

        var response = await factory.CreateUserClient(officer: true).PostAsJsonAsync("/api/records", ServiceRecord(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, await factory.ScalarAsync<long>("SELECT count(*) FROM record"));
        Assert.Equal(1, await factory.ScalarAsync<long>("SELECT count(*) FROM account"));
    }

    [Fact]
    public async Task AnAccountFromAnotherTenant_IsNotAMatch()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var oid = Guid.NewGuid();
        await factory.CreateUserClient(oid, tid: Guid.NewGuid(), name: "Other tenant").GetAsync("/api/me");

        var response = await factory.CreateUserClient(officer: true).PostAsJsonAsync("/api/records", ServiceRecord(oid));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task WhenTheDirectoryIsUnavailable_Returns503_AndCreatesNothing()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        factory.Directory.Unavailable = true;

        var response = await factory.CreateUserClient(officer: true).PostAsJsonAsync("/api/records", ServiceRecord(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, await factory.ScalarAsync<long>("SELECT count(*) FROM record"));
    }

    [Fact]
    public async Task NullSubject_MeansSelf_ForOfficersAndCitizens()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);

        var citizen = await (await factory.CreateUserClient().PostAsJsonAsync("/api/records", ServiceRecord(null))).Content.ReadFromJsonAsync<RecordDto>();
        var officer = await (await factory.CreateUserClient(officer: true).PostAsJsonAsync("/api/records", ServiceRecord(null))).Content.ReadFromJsonAsync<RecordDto>();

        Assert.False(citizen!.IsServiceRecord);
        Assert.False(officer!.IsServiceRecord);
        Assert.Equal(officer.SubjectAccountId, officer.AuthorAccountId);
    }

    [Fact]
    public async Task NamingYourselfAsSubject_CreatesAnOrdinaryRecord_AndNoNotification()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var oid = Guid.NewGuid();
        var officer = factory.CreateUserClient(oid, officer: true);

        var record = await (await officer.PostAsJsonAsync("/api/records", ServiceRecord(oid))).Content.ReadFromJsonAsync<RecordDto>();

        Assert.False(record!.IsServiceRecord);
        Assert.Equal(0, await factory.ScalarAsync<long>("SELECT count(*) FROM notification"));
        Assert.Equal(0, factory.Directory.Calls);
    }

    [Fact]
    public async Task Subject_CanReadTheServiceRecord_ButNotEditIt()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var citizen = await SignedInAsync(factory);
        var officer = factory.CreateUserClient(officer: true, name: "Officer");
        var created = (await (await officer.PostAsJsonAsync("/api/records", ServiceRecord(citizen.Oid))).Content.ReadFromJsonAsync<RecordDto>())!;

        var read = await citizen.Client.GetAsync($"/api/records/{created.Id}");
        var record = await read.Content.ReadFromJsonAsync<RecordDto>();
        var edit = await citizen.Client.PutAsJsonAsync($"/api/records/{created.Id}", new UpdateRecordRequest(
            "Tampered", created.Affiliation, null, null, created.EngagementDetail, null, null, created.Version));

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.True(record!.IsServiceRecord);
        Assert.False(record.CanEdit);
        Assert.Equal("Officer", record.AuthorDisplayName);
        Assert.Equal(HttpStatusCode.Forbidden, edit.StatusCode);
        var unchanged = await (await officer.GetAsync($"/api/records/{created.Id}")).Content.ReadFromJsonAsync<RecordDto>();
        Assert.Equal("Commendation", unchanged!.Title);
    }

    [Fact]
    public async Task Subject_SeesTheServiceRecordInTheirOwnList_AndNoAccessLogRowIsWritten()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var citizen = await SignedInAsync(factory);
        await factory.CreateUserClient(officer: true).PostAsJsonAsync("/api/records", ServiceRecord(citizen.Oid, "Mentioned in dispatches"));

        var page = await (await citizen.Client.GetAsync("/api/records")).Content.ReadFromJsonAsync<PagedResult<RecordSummaryDto>>();
        await citizen.Client.GetAsync($"/api/records/{page!.Items[0].Id}");

        Assert.Equal(["Mentioned in dispatches"], page.Items.Select(r => r.Title));
        Assert.True(page.Items[0].IsServiceRecord);
        Assert.Equal(0, await factory.ScalarAsync<long>("SELECT count(*) FROM record_access_log"));
    }

    [Fact]
    public async Task Officer_CanStillEditTheServiceRecord_AndSubjectSeesTheOfficerInHistory()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var citizen = await SignedInAsync(factory);
        var officer = factory.CreateUserClient(officer: true, name: "Officer");
        var created = (await (await officer.PostAsJsonAsync("/api/records", ServiceRecord(citizen.Oid))).Content.ReadFromJsonAsync<RecordDto>())!;

        var response = await officer.PutAsJsonAsync($"/api/records/{created.Id}", new UpdateRecordRequest(
            "Updated", created.Affiliation, null, null, created.EngagementDetail, null, null, created.Version));
        var history = await (await citizen.Client.GetAsync($"/api/records/{created.Id}/history")).Content.ReadFromJsonAsync<PagedResult<RevisionDto>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["Officer", "Officer"], history!.Items.Select(r => r.ChangedByDisplayName));
    }
}
