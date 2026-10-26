using System.Net;
using System.Text;
using Gisr.Api.Accounts;

namespace Gisr.Api.Tests.Accounts;

public class GraphTenantDirectoryTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private sealed class FixedToken(string token = "graph-token") : IGraphTokenProvider
    {
        public Task<string> GetTokenAsync(CancellationToken cancellationToken) => Task.FromResult(token);
    }

    private sealed class FailingToken : IGraphTokenProvider
    {
        public Task<string> GetTokenAsync(CancellationToken cancellationToken) =>
            throw new DirectoryUnavailableException("consent required");
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static GraphTenantDirectory Directory(StubHandler handler, IGraphTokenProvider? tokens = null) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://graph.microsoft.com/v1.0/") }, tokens ?? new FixedToken());

    private static string Query(HttpRequestMessage request, string key) =>
        System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query)[key]!;

    [Fact]
    public async Task Search_AsksGraphForTheTermAcrossNameAndEmail_WithTheOfficersToken()
    {
        var handler = new StubHandler(_ => Json("""{"value":[]}"""));

        await Directory(handler).SearchAsync("luke", 7, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/v1.0/users", request.RequestUri!.AbsolutePath);
        Assert.Equal("\"displayName:luke\" OR \"mail:luke\" OR \"userPrincipalName:luke\"", Query(request, "$search"));
        Assert.Equal("id,displayName,mail,userPrincipalName", Query(request, "$select"));
        Assert.Equal("7", Query(request, "$top"));
        Assert.Equal("Bearer graph-token", request.Headers.Authorization?.ToString());
        Assert.Equal("eventual", Assert.Single(request.Headers.GetValues("ConsistencyLevel")));
    }

    [Fact]
    public async Task Search_RemovesCharactersThatCouldBreakOutOfTheSearchExpression()
    {
        var handler = new StubHandler(_ => Json("""{"value":[]}"""));

        await Directory(handler).SearchAsync("a\"b\\c\r\nd", 5, CancellationToken.None);

        Assert.Equal("\"displayName:abcd\" OR \"mail:abcd\" OR \"userPrincipalName:abcd\"", Query(handler.Requests[0], "$search"));
    }

    [Fact]
    public async Task Search_ReturnsUsersByName_FallingBackToThePrincipalNameForEmail()
    {
        var luke = Guid.NewGuid();
        var anakin = Guid.NewGuid();
        var handler = new StubHandler(_ => Json($$"""
            {"value":[
              {"id":"{{luke}}","displayName":"Luke Skywalker","mail":null,"userPrincipalName":"luke@rebels.example"},
              {"id":"{{anakin}}","displayName":"Anakin Skywalker","mail":"anakin@jedi.example","userPrincipalName":"anakin@x.example"}
            ]}
            """));

        var users = await Directory(handler).SearchAsync("skywalker", 10, CancellationToken.None);

        Assert.Equal(
            [new DirectoryUser(anakin, "Anakin Skywalker", "anakin@jedi.example"), new DirectoryUser(luke, "Luke Skywalker", "luke@rebels.example")],
            users);
    }

    [Fact]
    public async Task Search_IgnoresEntriesWithoutAnIdOrName()
    {
        var good = Guid.NewGuid();
        var handler = new StubHandler(_ => Json($$"""
            {"value":[
              {"id":"not-a-guid","displayName":"Broken Id"},
              {"id":"{{Guid.NewGuid()}}","displayName":null},
              {"displayName":"No Id"},
              {"id":"{{good}}","displayName":"Valid"}
            ]}
            """));

        var users = await Directory(handler).SearchAsync("x", 10, CancellationToken.None);

        Assert.Equal(good, Assert.Single(users).ObjectId);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Search_WhenGraphFails_ReportsTheDirectoryAsUnavailable_WithoutLeakingTheBody(HttpStatusCode status)
    {
        var handler = new StubHandler(_ => Json("""{"error":{"message":"secret tenant details"}}""", status));

        var ex = await Assert.ThrowsAsync<DirectoryUnavailableException>(() => Directory(handler).SearchAsync("x", 5, CancellationToken.None));

        Assert.DoesNotContain("secret tenant details", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_WhenNoTokenCanBeAcquired_ReportsTheDirectoryAsUnavailable_WithoutCallingGraph()
    {
        var handler = new StubHandler(_ => Json("""{"value":[]}"""));

        await Assert.ThrowsAsync<DirectoryUnavailableException>(() =>
            Directory(handler, new FailingToken()).SearchAsync("x", 5, CancellationToken.None));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Get_ReadsOneUserById()
    {
        var id = Guid.NewGuid();
        var handler = new StubHandler(_ => Json($$"""{"id":"{{id}}","displayName":"Padme Amidala","mail":"padme@naboo.example","userPrincipalName":"p@x"}"""));

        var user = await Directory(handler).GetAsync(id, CancellationToken.None);

        Assert.Equal(new DirectoryUser(id, "Padme Amidala", "padme@naboo.example"), user);
        var request = Assert.Single(handler.Requests);
        Assert.Equal($"/v1.0/users/{id}", request.RequestUri!.AbsolutePath);
        Assert.Equal("id,displayName,mail,userPrincipalName", Query(request, "$select"));
        Assert.Equal("Bearer graph-token", request.Headers.Authorization?.ToString());
    }

    [Fact]
    public async Task Get_ReturnsNull_WhenTheUserDoesNotExist()
    {
        var handler = new StubHandler(_ => Json("""{"error":{"code":"Request_ResourceNotFound"}}""", HttpStatusCode.NotFound));

        Assert.Null(await Directory(handler).GetAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Get_WhenGraphFails_ReportsTheDirectoryAsUnavailable()
    {
        var handler = new StubHandler(_ => Json("{}", HttpStatusCode.ServiceUnavailable));

        await Assert.ThrowsAsync<DirectoryUnavailableException>(() => Directory(handler).GetAsync(Guid.NewGuid(), CancellationToken.None));
    }
}
