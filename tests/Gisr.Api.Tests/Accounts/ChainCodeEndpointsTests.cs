using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gisr.Api.Tests.Common;
using Microsoft.AspNetCore.Mvc;

namespace Gisr.Api.Tests.Accounts;

[Collection("api")]
public class ChainCodeEndpointsTests(PostgresFixture postgres)
{
    private const string Path = "/api/me/chain-code";

    [Fact]
    public async Task Profile_SaveReloadChangeDelete_IsOptionalAndOwnerScoped()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var owner = factory.CreateUserClient();
        var other = factory.CreateUserClient(officer: true);

        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(Path)).StatusCode);
        var saved = await owner.PutAsJsonAsync(Path, new { fullName = "Leia Organa", homeworld = " Alderaan ", species = "Human", birthDate = "19 BBY", eyeColor = "Brown" });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var profile = await saved.Content.ReadFromJsonAsync<JsonElement>();
        var code = profile.GetProperty("chainCode").GetString();
        Assert.Matches(@"^\d{5}(-\d{5}){7}$", code!);
        Assert.Equal("Alderaan", profile.GetProperty("profile").GetProperty("homeworld").GetString());
        Assert.Equal(profile, await (await owner.GetAsync(Path)).Content.ReadFromJsonAsync<JsonElement>(), JsonElementComparer.Instance);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(Path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync(Path)).StatusCode);

        var again = await owner.PutAsJsonAsync(Path, new { fullName = "Leia Organa", homeworld = "Alderaan", species = "Human", birthDate = "19 BBY", eyeColor = "Brown" });
        Assert.Equal(code, (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("chainCode").GetString());
        var changed = await owner.PutAsJsonAsync(Path, new { fullName = "Leia Organa", homeworld = "Chandrila" });
        Assert.NotEqual(code, (await changed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("chainCode").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync(Path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(Path)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/me")).StatusCode);
    }

    [Theory]
    [InlineData("eyeColors", "eyeColor")]
    [InlineData("skinColors", "skinColor")]
    [InlineData("hairColors", "hairColor")]
    public async Task Profile_ColorSets_SaveReloadNormalizeAndPreserveLegacyCode(string collection, string scalar)
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var owner = factory.CreateUserClient();
        var legacy = await owner.PutAsJsonAsync(Path, new Dictionary<string, object> { [scalar] = "Blue" });
        Assert.Equal(HttpStatusCode.OK, legacy.StatusCode);
        var legacyCode = (await legacy.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("chainCode").GetString();
        var single = await owner.PutAsJsonAsync(Path, new Dictionary<string, object> { [collection] = new[] { "Blue" } });
        Assert.Equal(legacyCode, (await single.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("chainCode").GetString());

        var saved = await owner.PutAsJsonAsync(Path, new Dictionary<string, object> { [collection] = new[] { "Green", "Blue", "Green" } });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var profile = await saved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(new[] { "Blue", "Green" }, profile.GetProperty("profile").GetProperty(collection).EnumerateArray().Select(color => color.GetString()));
        Assert.NotEqual(legacyCode, profile.GetProperty("chainCode").GetString());
        Assert.Equal(profile, await (await owner.GetAsync(Path)).Content.ReadFromJsonAsync<JsonElement>(), JsonElementComparer.Instance);
        var again = await owner.PutAsJsonAsync(Path, new Dictionary<string, object> { [collection] = new[] { "Blue", "Green" } });
        Assert.Equal(profile.GetProperty("chainCode").GetString(), (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("chainCode").GetString());
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task Profile_Anonymous_Returns401(string method)
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        using var request = new HttpRequestMessage(new HttpMethod(method), Path);
        if (method == "PUT") request.Content = JsonContent.Create(new { fullName = "Citizen" });
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().SendAsync(request)).StatusCode);
    }

    [Theory]
    [InlineData("{}", "profile")]
    [InlineData("{\"fullName\":\"   \"}", "profile")]
    [InlineData("{\"eyeColor\":999}", "eyeColor")]
    [InlineData("{\"eyeColors\":[999]}", "eyeColors")]
    [InlineData("{\"skinColors\":[999]}", "skinColors")]
    [InlineData("{\"hairColors\":[999]}", "hairColors")]
    [InlineData("{\"eyeColors\":[],\"skinColors\":[],\"hairColors\":[]}", "profile")]
    public async Task Profile_Invalid_ReturnsValidationProblem(string json, string field)
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var response = await factory.CreateUserClient().PutAsync(Path, new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains(field, problem!.Errors.Keys);
    }

    [Fact]
    public async Task Profile_OverlongAndForgedFields_CannotChangeIdentity()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateUserClient();
        var invalid = await client.PutAsJsonAsync(Path, new { fullName = new string('x', 201), biography = new string('x', 4001) });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var errors = await invalid.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains("fullName", errors!.Errors.Keys);
        Assert.Contains("biography", errors.Errors.Keys);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Path)).StatusCode);
        var saved = await client.PutAsJsonAsync(Path, new { fullName = "Citizen", accountId = Guid.NewGuid(), operatingNumber = "FORGED", chainCode = "FORGED" });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.NotEqual("FORGED", (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("chainCode").GetString());
        var account = await (await client.GetAsync("/api/me")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, account.GetProperty("operatingNumber").ValueKind);
    }

    private sealed class JsonElementComparer : IEqualityComparer<JsonElement>
    {
        public static readonly JsonElementComparer Instance = new();
        public bool Equals(JsonElement left, JsonElement right) => JsonElement.DeepEquals(left, right);
        public int GetHashCode(JsonElement value) => 0;
    }
}