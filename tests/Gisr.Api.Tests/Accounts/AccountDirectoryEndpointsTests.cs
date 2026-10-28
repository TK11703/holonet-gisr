using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gisr.Api.Tests.Common;
using Gisr.Shared.Accounts;
using Microsoft.AspNetCore.Mvc;

namespace Gisr.Api.Tests.Accounts;

[Collection("api")]
public class AccountDirectoryEndpointsTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Anonymous_Returns401()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);

        var response = await factory.CreateClient().GetAsync("/api/accounts?query=lu");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task NonOfficer_Returns403_WithoutTouchingTheDirectory()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);

        var response = await factory.CreateUserClient().GetAsync("/api/accounts?query=lu");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, factory.Directory.Calls);
    }

    [Fact]
    public async Task Officer_FindsTenantUsers_EvenWhenTheyNeverSignedIn()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var luke = factory.Directory.Add("Luke Skywalker", "luke@rebels.example");
        var anakin = factory.Directory.Add("Anakin Skywalker", "anakin@jedi.example");
        factory.Directory.Add("Leia Organa");

        var response = await factory.CreateUserClient(officer: true).GetAsync("/api/accounts?query=SKYWALKER");
        var users = (await response.Content.ReadFromJsonAsync<List<DirectoryUserDto>>())!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            [new DirectoryUserDto(anakin, "Anakin Skywalker", "anakin@jedi.example"), new DirectoryUserDto(luke, "Luke Skywalker", "luke@rebels.example")],
            users);
        Assert.Equal(0, await factory.ScalarAsync<long>("SELECT count(*) FROM account WHERE display_name LIKE '%Skywalker%'"));
    }

    [Fact]
    public async Task Officer_FindsKnownAccounts_ByOperatingNumberPrefix_WithoutDuplicates()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var oid = Guid.NewGuid();
        (await factory.CreateUserClient(oid, name: "Rex").GetAsync("/api/me")).EnsureSuccessStatusCode();
        await factory.ScalarAsync<int>($"UPDATE account SET operating_number = 'CT-7567' WHERE entra_object_id = '{oid}' RETURNING 1");
        factory.Directory.Add("Other Person");

        var users = (await factory.CreateUserClient(officer: true).GetFromJsonAsync<List<DirectoryUserDto>>("/api/accounts?query=ct-75"))!;

        Assert.Equal(oid, Assert.Single(users).ObjectId);
    }

    [Fact]
    public async Task Search_TrimsTheQuery_BeforeAskingTheDirectory()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        factory.Directory.Add("Han Solo");

        var users = (await factory.CreateUserClient(officer: true).GetFromJsonAsync<List<DirectoryUserDto>>("/api/accounts?query=%20han%20"))!;

        Assert.Single(users);
    }

    [Fact]
    public async Task Results_ExposeOnlyObjectIdNameAndEmail()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        factory.Directory.Add("Han Solo", "han@falcon.example");

        var json = await factory.CreateUserClient(officer: true).GetStringAsync("/api/accounts?query=han");
        using var document = JsonDocument.Parse(json);

        var names = document.RootElement[0].EnumerateObject().Select(p => p.Name).Order().ToArray();
        Assert.Equal(["displayName", "email", "objectId"], names);
    }

    [Fact]
    public async Task PageSize_LimitsTheResults()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        factory.Directory.Add("Clone 1");
        factory.Directory.Add("Clone 2");
        factory.Directory.Add("Clone 3");

        var users = (await factory.CreateUserClient(officer: true).GetFromJsonAsync<List<DirectoryUserDto>>("/api/accounts?query=clone&pageSize=2"))!;

        Assert.Equal(["Clone 1", "Clone 2"], users.Select(u => u.DisplayName));
    }

    [Theory]
    [InlineData("")]
    [InlineData("?query=")]
    [InlineData("?query=a")]
    [InlineData("?query=%20a%20")]
    [InlineData("?query=ab&pageSize=0")]
    [InlineData("?query=ab&pageSize=26")]
    public async Task InvalidQuery_Returns400(string query)
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);

        var response = await factory.CreateUserClient(officer: true).GetAsync($"/api/accounts{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, factory.Directory.Calls);
    }

    [Fact]
    public async Task QueryLongerThan100Characters_Returns400()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);

        var response = await factory.CreateUserClient(officer: true).GetAsync($"/api/accounts?query={new string('x', 101)}");
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("query", problem!.Errors.Keys);
    }

    [Fact]
    public async Task WhenTheDirectoryIsUnavailable_Returns503_WithoutDetails()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        factory.Directory.Unavailable = true;

        var response = await factory.CreateUserClient(officer: true).GetAsync("/api/accounts?query=lu");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("The directory is down", body, StringComparison.Ordinal);
    }
}
