using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gisr.Api.Tests.Common;
using Microsoft.AspNetCore.Mvc;

namespace Gisr.Api.Tests.Accounts;

[Collection("api")]
public class AccountAffiliationEndpointsTests(PostgresFixture postgres)
{
    private const string Path = "/api/me/affiliations";

    [Fact]
    public async Task Options_AreGroupedByCategory()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var response = await factory.CreateUserClient().GetAsync("/api/me/affiliation-options");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var groups = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(groups.EnumerateArray(), group => group.GetProperty("category").GetString() == "Military Organizations");
        Assert.Contains(groups.EnumerateArray().SelectMany(group => group.GetProperty("names").EnumerateArray()), name => name.GetString() == "Galactic Empire");
    }

    [Fact]
    public async Task AddListDelete_IsOwnerScoped_AndNormalizesPrefixAndNumber()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var owner = factory.CreateUserClient();
        var other = factory.CreateUserClient(officer: true);

        Assert.Equal(0, (await (await owner.GetAsync(Path)).Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength());
        var created = await owner.PostAsJsonAsync(Path, new
        {
            primary = " Galactic Empire ", secondary = "Stormtrooper Corps", specialUnit = "501st Legion", prefix = "tk", operatingNumber = "11703",
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var item = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Galactic Empire", item.GetProperty("primary").GetString());
        Assert.Equal("TK", item.GetProperty("prefix").GetString());
        var id = item.GetProperty("id").GetGuid();

        var minimal = await owner.PostAsJsonAsync(Path, new { primary = "Black Sun", secondary = "  ", prefix = "" });
        Assert.Equal(HttpStatusCode.Created, minimal.StatusCode);
        var minimalItem = await minimal.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, minimalItem.GetProperty("secondary").ValueKind);
        Assert.Equal(JsonValueKind.Null, minimalItem.GetProperty("prefix").ValueKind);

        Assert.Equal(2, (await (await owner.GetAsync(Path)).Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength());
        Assert.Equal(0, (await (await other.GetAsync(Path)).Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"{Path}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"{Path}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.DeleteAsync($"{Path}/{id}")).StatusCode);
        Assert.Equal(1, (await (await owner.GetAsync(Path)).Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength());
    }

    [Fact]
    public async Task Add_SameHierarchyTwice_ReturnsConflict_ButNumbersMayRepeatAcrossDifferentHierarchies()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateUserClient();
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(Path, new { primary = "Galactic Empire", secondary = "Imperial Navy" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(Path, new { primary = "Galactic Empire", secondary = "Imperial Navy", prefix = "TK", operatingNumber = "1" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(Path, new { primary = "Galactic Empire" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(Path, new { primary = "Galactic Empire" })).StatusCode);
    }

    [Theory]
    [InlineData("{}", "primary")]
    [InlineData("{\"primary\":\"Not In The List\"}", "primary")]
    [InlineData("{\"primary\":\"Galactic Empire\",\"secondary\":\"%LONG%\"}", "secondary")]
    [InlineData("{\"primary\":\"Galactic Empire\",\"secondary\":\"Not In The List\"}", "secondary")]
    [InlineData("{\"primary\":\"Galactic Empire\",\"specialUnit\":\"%LONG%\"}", "specialUnit")]
    [InlineData("{\"primary\":\"Galactic Empire\",\"prefix\":\"TOOLONGPREFIX\",\"operatingNumber\":\"1\"}", "prefix")]
    [InlineData("{\"primary\":\"Galactic Empire\",\"prefix\":\"T K\",\"operatingNumber\":\"1\"}", "prefix")]
    [InlineData("{\"primary\":\"Galactic Empire\",\"operatingNumber\":\"11 703\"}", "operatingNumber")]
    [InlineData("{\"primary\":\"Galactic Empire\",\"prefix\":\"TK\"}", "operatingNumber")]
    public async Task Add_Invalid_ReturnsValidationProblem(string json, string field)
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var body = json.Replace("%LONG%", new string('x', 101), StringComparison.Ordinal);
        var response = await factory.CreateUserClient().PostAsync(Path, new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains(field, problem!.Errors.Keys);
    }

    [Fact]
    public async Task Add_BeyondLimit_ReturnsConflict()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateUserClient();
        var names = Gisr.Api.Accounts.AffiliationOptionCatalog.Groups.SelectMany(group => group.Names).Distinct().Take(26).ToArray();
        for (var index = 0; index < 25; index++)
        {
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(Path, new { primary = names[index] })).StatusCode);
        }

        var response = await client.PostAsJsonAsync(Path, new { primary = names[25] });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Add_ForgedOwnerFields_AreIgnored()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var owner = factory.CreateUserClient();
        var other = factory.CreateUserClient(officer: true);
        var response = await owner.PostAsJsonAsync(Path, new { primary = "Galactic Empire", accountId = Guid.NewGuid(), ownerId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, (await (await owner.GetAsync(Path)).Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength());
        Assert.Equal(0, (await (await other.GetAsync(Path)).Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength());
    }

    [Theory]
    [InlineData("GET", "/api/me/affiliations")]
    [InlineData("POST", "/api/me/affiliations")]
    [InlineData("DELETE", "/api/me/affiliations/6f2f3e0e-6a2c-4d58-9d7b-0d2a6c2f9c11")]
    [InlineData("GET", "/api/me/affiliation-options")]
    public async Task Anonymous_Returns401(string method, string path)
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST") request.Content = JsonContent.Create(new { primary = "Galactic Empire" });
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().SendAsync(request)).StatusCode);
    }
}
