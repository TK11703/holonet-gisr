using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Gisr.Web.Common;

namespace Gisr.Web.Tests.Common;

public class ApiClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }

    private sealed class FakeTokens : IAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) => Task.FromResult("token-123");
    }

    private static (ApiClient Client, StubHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") };
        return (new ApiClient(http, new FakeTokens()), handler);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json, string mediaType = "application/json") =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, mediaType) };

    public sealed record Item(string Name);

    [Fact]
    public async Task Get_AttachesBearerToken_AndDeserializesCamelCase()
    {
        var (client, handler) = Create(_ => Json(HttpStatusCode.OK, """{"name":"Han"}"""));

        var item = await client.GetAsync<Item>("api/items/1");

        Assert.Equal("Han", item?.Name);
        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization?.Scheme);
        Assert.Equal("token-123", handler.LastRequest.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task Get_SendsExtraHeaders()
    {
        var (client, handler) = Create(_ => Json(HttpStatusCode.OK, """{"name":"x"}"""));

        await client.GetAsync<Item>("api/items/1", new Dictionary<string, string> { ["X-View-Token"] = "abc" });

        Assert.Equal("abc", handler.LastRequest!.Headers.GetValues("X-View-Token").Single());
    }

    [Fact]
    public async Task Post_SerializesBodyAsCamelCaseJson()
    {
        var (client, handler) = Create(_ => Json(HttpStatusCode.Created, """{"name":"Leia"}"""));

        await client.PostAsync<Item>("api/items", new Item("Leia"));

        using var doc = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("Leia", doc.RootElement.GetProperty("name").GetString());
        Assert.Equal("application/json", handler.LastRequest!.Content!.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task NoContent_ReturnsDefault()
    {
        var (client, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        Assert.Null(await client.GetAsync<Item>("api/items/1"));
    }

    [Fact]
    public async Task ProblemDetails_MapsToApiException()
    {
        var (client, _) = Create(_ => Json(HttpStatusCode.NotFound,
            """{"title":"Not Found","status":404,"detail":"No such record"}""", "application/problem+json"));

        var ex = await Assert.ThrowsAsync<ApiException>(() => client.GetAsync<Item>("api/items/1"));

        Assert.Equal(404, ex.Status);
        Assert.Equal("Not Found", ex.Title);
        Assert.Equal("No such record", ex.Detail);
    }

    [Fact]
    public async Task ValidationProblem_ExposesFieldErrors()
    {
        var (client, _) = Create(_ => Json(HttpStatusCode.BadRequest,
            """{"title":"Validation","status":400,"errors":{"title":["Required"],"affiliation":["Unknown","Bad"]}}""", "application/problem+json"));

        var ex = await Assert.ThrowsAsync<ApiException>(() => client.PostAsync<Item>("api/items", new Item("")));

        Assert.Equal(["Required"], ex.Errors["title"]);
        Assert.Equal(2, ex.Errors["affiliation"].Length);
    }

    [Fact]
    public async Task ExtensionMembers_AreExposed()
    {
        var (client, _) = Create(_ => Json(HttpStatusCode.Forbidden,
            """{"title":"Forbidden","status":403,"sealed":true,"attemptsRemaining":3}""", "application/problem+json"));

        var ex = await Assert.ThrowsAsync<ApiException>(() => client.GetAsync<Item>("api/items/1"));

        Assert.True(ex.IsSealed);
        Assert.Equal(3, ex.Extensions["attemptsRemaining"].GetInt32());
    }

    [Fact]
    public async Task RetryAfter_IsExposed()
    {
        var (client, _) = Create(_ =>
        {
            var response = Json(HttpStatusCode.TooManyRequests, """{"title":"Too Many Requests","status":429}""", "application/problem+json");
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(7));
            return response;
        });

        var ex = await Assert.ThrowsAsync<ApiException>(() => client.PostAsync<Item>("api/items", new Item("x")));

        Assert.Equal(TimeSpan.FromMinutes(7), ex.RetryAfter);
    }

    [Fact]
    public async Task NonJsonError_StillProducesApiException()
    {
        var (client, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("oops") });

        var ex = await Assert.ThrowsAsync<ApiException>(() => client.GetAsync<Item>("api/items/1"));

        Assert.Equal(502, ex.Status);
    }
}
