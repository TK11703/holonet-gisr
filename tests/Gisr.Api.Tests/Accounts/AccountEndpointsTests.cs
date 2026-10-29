using System.Net;
using System.Net.Http.Json;
using Gisr.Api.Tests.Common;
using Gisr.Shared.Accounts;
using Microsoft.AspNetCore.Mvc;

namespace Gisr.Api.Tests.Accounts;

[Collection("api")]
public class AccountEndpointsTests(PostgresFixture postgres)
{
    [Fact]
    public async Task OperatingNumber_DirectoryRefreshCreatesUpdatesClearsAndPreservesOnFailure()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var objectId = Guid.NewGuid();
        var client = factory.CreateUserClient(oid: objectId);
        factory.Identity.Numbers[objectId] = "TK-11703";
        Assert.Equal("TK-11703", (await client.GetFromJsonAsync<AccountDto>("/api/me"))!.OperatingNumber);
        factory.Identity.Numbers[objectId] = "CT-7567";
        Assert.Equal("CT-7567", (await client.GetFromJsonAsync<AccountDto>("/api/me"))!.OperatingNumber);
        factory.Identity.Unavailable = true;
        Assert.Equal("CT-7567", (await client.GetFromJsonAsync<AccountDto>("/api/me"))!.OperatingNumber);
        var refreshCalls = factory.Identity.Calls;
        var settings = await client.PutAsJsonAsync("/api/me/settings", new
        {
            displayName = "Rex", notifyOnServiceRecord = true, operatingNumber = "FORGED",
        });
        Assert.Equal("CT-7567", (await settings.Content.ReadFromJsonAsync<AccountDto>())!.OperatingNumber);
        Assert.Equal(refreshCalls, factory.Identity.Calls);
        factory.Identity.Unavailable = false;
        factory.Identity.Numbers[objectId] = null;
        Assert.Null((await client.GetFromJsonAsync<AccountDto>("/api/me"))!.OperatingNumber);
    }

    [Fact]
    public async Task GetMe_Anonymous_Returns401()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);

        var response = await factory.CreateClient().GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMe_TokenWithoutRequiredScope_Returns403()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateUserClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.NoScopeHeader, "1");

        var response = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetMe_FirstCall_CreatesAccount_WithDefaults()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateUserClient(name: "Rey");

        var response = await client.GetAsync("/api/me");
        var account = await response.Content.ReadFromJsonAsync<AccountDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Rey", account?.DisplayName);
        Assert.True(account?.NotifyOnServiceRecord);
        Assert.False(account?.IsOfficer);
        Assert.NotEqual(Guid.Empty, account?.Id);
    }

    [Fact]
    public async Task GetMe_SecondCall_ReturnsSameAccount()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateUserClient();

        var first = await (await client.GetAsync("/api/me")).Content.ReadFromJsonAsync<AccountDto>();
        var second = await (await client.GetAsync("/api/me")).Content.ReadFromJsonAsync<AccountDto>();

        Assert.Equal(first?.Id, second?.Id);
    }

    [Fact]
    public async Task GetMe_WithOfficerRole_ReportsIsOfficer()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);

        var account = await (await factory.CreateUserClient(officer: true).GetAsync("/api/me")).Content.ReadFromJsonAsync<AccountDto>();

        Assert.True(account?.IsOfficer);
    }

    [Fact]
    public async Task PutSettings_PersistsChanges()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateUserClient();
        await client.GetAsync("/api/me");

        var response = await client.PutAsJsonAsync("/api/me/settings",
            new UpdateAccountSettingsRequest("Commander Cody", "cody@clone.example", false));
        var updated = await response.Content.ReadFromJsonAsync<AccountDto>();
        var reloaded = await (await client.GetAsync("/api/me")).Content.ReadFromJsonAsync<AccountDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Commander Cody", updated?.DisplayName);
        Assert.Equal("Commander Cody", reloaded?.DisplayName);
        Assert.Equal("cody@clone.example", reloaded?.ContactEmail);
        Assert.False(reloaded?.NotifyOnServiceRecord);
    }

    [Fact]
    public async Task GetMe_NewAccount_HasTheDefaultTheme()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);

        var account = await (await factory.CreateUserClient().GetAsync("/api/me")).Content.ReadFromJsonAsync<AccountDto>();

        Assert.Equal("teal", account?.Theme);
    }

    [Fact]
    public async Task PutSettings_Theme_IsSavedAndKeptWhenOmitted()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateUserClient();
        await client.GetAsync("/api/me");

        var chosen = await (await client.PutAsJsonAsync("/api/me/settings",
            new UpdateAccountSettingsRequest("Cody", null, true, "hub"))).Content.ReadFromJsonAsync<AccountDto>();
        await client.PutAsJsonAsync("/api/me/settings", new UpdateAccountSettingsRequest("Cody", null, true));
        var reloaded = await (await client.GetAsync("/api/me")).Content.ReadFromJsonAsync<AccountDto>();

        Assert.Equal("hub", chosen?.Theme);
        Assert.Equal("hub", reloaded?.Theme);
    }

    [Fact]
    public async Task PutSettings_UnknownTheme_Returns400AndKeepsTheSavedTheme()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateUserClient();
        await client.GetAsync("/api/me");

        var response = await client.PutAsJsonAsync("/api/me/settings",
            new UpdateAccountSettingsRequest("Cody", null, true, "purple"));
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        var reloaded = await (await client.GetAsync("/api/me")).Content.ReadFromJsonAsync<AccountDto>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("theme", problem!.Errors.Keys);
        Assert.Equal("teal", reloaded?.Theme);
    }

    [Fact]
    public async Task PutSettings_ChangesOnlyTheCallersOwnAccount()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var alice = factory.CreateUserClient(name: "Alice");
        var bob = factory.CreateUserClient(name: "Bob");
        await alice.GetAsync("/api/me");
        await bob.GetAsync("/api/me");

        await alice.PutAsJsonAsync("/api/me/settings", new UpdateAccountSettingsRequest("Alice Renamed", null, true));

        var bobAccount = await (await bob.GetAsync("/api/me")).Content.ReadFromJsonAsync<AccountDto>();
        Assert.Equal("Bob", bobAccount?.DisplayName);
    }

    [Fact]
    public async Task PutSettings_InvalidInput_Returns400WithFieldErrors()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateUserClient();

        var response = await client.PutAsJsonAsync("/api/me/settings",
            new UpdateAccountSettingsRequest("", "not-an-email", true));
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("displayName", problem!.Errors.Keys);
        Assert.Contains("contactEmail", problem.Errors.Keys);
    }

    [Fact]
    public async Task PutSettings_Anonymous_Returns401()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);

        var response = await factory.CreateClient().PutAsJsonAsync("/api/me/settings",
            new UpdateAccountSettingsRequest("x", null, true));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
