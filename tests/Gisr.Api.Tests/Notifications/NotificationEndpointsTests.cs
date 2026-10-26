using System.Net;
using System.Net.Http.Json;
using Gisr.Api.Tests.Common;
using Gisr.Shared.Accounts;
using Gisr.Shared.Notifications;
using Gisr.Shared.Records;

namespace Gisr.Api.Tests.Notifications;

[Collection("api")]
public class NotificationEndpointsTests(PostgresFixture postgres)
{
    private static async Task<(HttpClient Client, Guid Oid)> CitizenAsync(ApiFactory factory, string name = "Citizen")
    {
        var oid = Guid.NewGuid();
        var client = factory.CreateUserClient(oid, name: name);
        await client.GetAsync("/api/me");
        return (client, oid);
    }

    private static async Task<RecordDto> ServiceRecordAsync(HttpClient officer, Guid subjectObjectId, string title = "Secret commendation")
    {
        var response = await officer.PostAsJsonAsync("/api/records", new CreateRecordRequest(
            title, "military", null, null,
            new Dictionary<string, string> { ["summary"] = "Details nobody should see", ["branch"] = "Navy", ["rank"] = "Admiral" },
            SubjectObjectId: subjectObjectId));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RecordDto>())!;
    }

    private static async Task<NotificationListDto> ListAsync(HttpClient client, string query = "")
    {
        var response = await client.GetAsync($"/api/notifications{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<NotificationListDto>())!;
    }

    [Fact]
    public async Task Anonymous_Returns401()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/notifications")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostAsync("/api/notifications/read-all", null)).StatusCode);
    }

    [Fact]
    public async Task NewUser_HasNoNotifications()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);

        var list = await ListAsync(factory.CreateUserClient());

        Assert.Equal(0, list.UnreadCount);
        Assert.Empty(list.Items);
    }

    [Fact]
    public async Task ServiceRecord_NotifiesTheSubject_WithoutRecordContent()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var citizen = await CitizenAsync(factory);
        var record = await ServiceRecordAsync(factory.CreateUserClient(officer: true, name: "Officer"), citizen.Oid);

        var list = await ListAsync(citizen.Client);

        Assert.Equal(1, list.UnreadCount);
        var item = Assert.Single(list.Items);
        Assert.Equal("service_record_created", item.Kind);
        Assert.Equal(record.Id, item.RecordId);
        Assert.False(item.IsRead);
        Assert.Contains("Officer", item.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Secret commendation", item.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("nobody should see", item.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OtherUsers_NeverSeeSomeoneElsesNotifications()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var citizen = await CitizenAsync(factory);
        await ServiceRecordAsync(factory.CreateUserClient(officer: true), citizen.Oid);

        var list = await ListAsync(factory.CreateUserClient());

        Assert.Empty(list.Items);
    }

    [Fact]
    public async Task List_IsNewestFirst_AndUnreadOnlyFilters()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var citizen = await CitizenAsync(factory);
        var officer = factory.CreateUserClient(officer: true);
        var first = await ServiceRecordAsync(officer, citizen.Oid, "one");
        var second = await ServiceRecordAsync(officer, citizen.Oid, "two");
        var all = await ListAsync(citizen.Client);
        await citizen.Client.PostAsync($"/api/notifications/{all.Items[1].Id}/read", null);

        var afterRead = await ListAsync(citizen.Client);
        var unread = await ListAsync(citizen.Client, "?unreadOnly=true");

        Assert.Equal([second.Id, first.Id], all.Items.Select(n => n.RecordId!.Value));
        Assert.Equal(1, afterRead.UnreadCount);
        Assert.Equal(2, afterRead.Items.Count);
        Assert.True(afterRead.Items[1].IsRead);
        Assert.Equal([second.Id], unread.Items.Select(n => n.RecordId!.Value));
    }

    [Fact]
    public async Task PageSize_LimitsItems_ButUnreadCountStaysTotal()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var citizen = await CitizenAsync(factory);
        var officer = factory.CreateUserClient(officer: true);
        for (var i = 0; i < 3; i++)
        {
            await ServiceRecordAsync(officer, citizen.Oid, $"r{i}");
        }

        var list = await ListAsync(citizen.Client, "?pageSize=2");

        Assert.Equal(2, list.Items.Count);
        Assert.Equal(3, list.UnreadCount);
    }

    [Fact]
    public async Task MarkRead_Returns204_AndReducesTheUnreadCount()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var citizen = await CitizenAsync(factory);
        await ServiceRecordAsync(factory.CreateUserClient(officer: true), citizen.Oid);
        var id = (await ListAsync(citizen.Client)).Items[0].Id;

        var response = await citizen.Client.PostAsync($"/api/notifications/{id}/read", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, (await ListAsync(citizen.Client)).UnreadCount);
    }

    [Fact]
    public async Task MarkRead_ForSomeoneElsesOrUnknownNotification_Returns404()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var citizen = await CitizenAsync(factory);
        await ServiceRecordAsync(factory.CreateUserClient(officer: true), citizen.Oid);
        var id = (await ListAsync(citizen.Client)).Items[0].Id;
        var stranger = factory.CreateUserClient();

        var foreign = await stranger.PostAsync($"/api/notifications/{id}/read", null);
        var unknown = await citizen.Client.PostAsync($"/api/notifications/{Guid.NewGuid()}/read", null);

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(1, (await ListAsync(citizen.Client)).UnreadCount);
    }

    [Fact]
    public async Task ReadAll_Returns204_AndMarksEverythingRead()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var citizen = await CitizenAsync(factory);
        var officer = factory.CreateUserClient(officer: true);
        await ServiceRecordAsync(officer, citizen.Oid, "a");
        await ServiceRecordAsync(officer, citizen.Oid, "b");

        var response = await citizen.Client.PostAsync("/api/notifications/read-all", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var list = await ListAsync(citizen.Client);
        Assert.Equal(0, list.UnreadCount);
        Assert.All(list.Items, n => Assert.True(n.IsRead));
    }

    [Fact]
    public async Task NoNotification_WhenTheSubjectTurnedThemOff()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var citizen = await CitizenAsync(factory);
        await citizen.Client.PutAsJsonAsync("/api/me/settings", new UpdateAccountSettingsRequest("Citizen", null, false));

        await ServiceRecordAsync(factory.CreateUserClient(officer: true), citizen.Oid);

        Assert.Empty((await ListAsync(citizen.Client)).Items);
    }

    [Fact]
    public async Task NoNotification_ForSelfAuthoredRecords()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var citizen = factory.CreateUserClient();
        await citizen.PostAsJsonAsync("/api/records", new CreateRecordRequest(
            "Mine", "civilian", null, null, new Dictionary<string, string> { ["summary"] = "x" }));

        Assert.Empty((await ListAsync(citizen)).Items);
    }

    [Fact]
    public async Task ATenantUserWhoNeverSignedIn_StillGetsTheNotification_AtFirstSignIn()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var oid = factory.Directory.Add("Padme Amidala");
        await ServiceRecordAsync(factory.CreateUserClient(officer: true, name: "Officer"), oid);

        var list = await ListAsync(factory.CreateUserClient(oid, name: "Padme"));

        Assert.Equal(1, list.UnreadCount);
        Assert.Contains("Officer", Assert.Single(list.Items).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=51")]
    public async Task InvalidPageSize_Returns400(string query)
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);

        var response = await factory.CreateUserClient().GetAsync($"/api/notifications{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
