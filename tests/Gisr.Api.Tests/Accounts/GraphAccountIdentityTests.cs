using System.Net;
using System.Text;
using Gisr.Api.Accounts;

namespace Gisr.Api.Tests.Accounts;

public class GraphAccountIdentityTests
{
    private sealed class FixedToken : IAccountIdentityTokenProvider
    {
        public Task<string> GetTokenAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult("identity-token");
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static GraphAccountIdentity Reader(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new Handler(respond)) { BaseAddress = new Uri("https://graph.microsoft.com/v1.0/") }, new FixedToken());

    [Theory]
    [InlineData("null", null)]
    [InlineData("\"\"", null)]
    [InlineData("\" CT-7567 \"", "CT-7567")]
    public async Task Read_UsesCurrentUserSelectionAndToken_AndAcceptsExplicitEmpty(string value, string? expected)
    {
        var objectId = Guid.NewGuid();
        var reader = Reader(request =>
        {
            Assert.Equal("/v1.0/me", request.RequestUri!.AbsolutePath);
            Assert.Equal("?$select=id,employeeId", Uri.UnescapeDataString(request.RequestUri.Query));
            Assert.Equal("Bearer identity-token", request.Headers.Authorization?.ToString());
            return Json($$"""{"id":"{{objectId}}","employeeId":{{value}}} """);
        });
        Assert.Equal(expected, await reader.GetOperatingNumberAsync(objectId, CancellationToken.None));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("not-json")]
    [InlineData("{\"id\":\"OID\"}")]
    [InlineData("{\"id\":\"OID\",\"employeeId\":23}")]
    [InlineData("{\"id\":\"00000000-0000-0000-0000-000000000000\",\"employeeId\":\"FORGED\"}")]
    public async Task Read_MissingMalformedAndMismatchedIdentity_IsUnavailable(string body)
    {
        var objectId = Guid.NewGuid();
        var reader = Reader(_ => Json(body.Replace("OID", objectId.ToString(), StringComparison.Ordinal)));
        await Assert.ThrowsAsync<DirectoryUnavailableException>(() => reader.GetOperatingNumberAsync(objectId, CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Read_FailedResponse_DoesNotLeakDirectoryContent(HttpStatusCode status)
    {
        var exception = await Assert.ThrowsAsync<DirectoryUnavailableException>(() =>
            Reader(_ => Json("secret tenant details", status)).GetOperatingNumberAsync(Guid.NewGuid(), CancellationToken.None));
        Assert.DoesNotContain("secret", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public async Task Read_EnforcesNumberLength(int length, bool valid)
    {
        var objectId = Guid.NewGuid();
        var value = new string('X', length);
        var reader = Reader(_ => Json($$"""{"id":"{{objectId}}","employeeId":"{{value}}"}"""));
        if (valid) Assert.Equal(value, await reader.GetOperatingNumberAsync(objectId, CancellationToken.None));
        else await Assert.ThrowsAsync<DirectoryUnavailableException>(() => reader.GetOperatingNumberAsync(objectId, CancellationToken.None));
    }

    [Fact]
    public async Task Read_TransportFailureAndTimeout_AreUnavailable_ButCallerCancellationPropagates()
    {
        await Assert.ThrowsAsync<DirectoryUnavailableException>(() =>
            Reader(_ => throw new HttpRequestException()).GetOperatingNumberAsync(Guid.NewGuid(), CancellationToken.None));
        await Assert.ThrowsAsync<DirectoryUnavailableException>(() =>
            Reader(_ => throw new TaskCanceledException()).GetOperatingNumberAsync(Guid.NewGuid(), CancellationToken.None));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Reader(_ => throw new InvalidOperationException("Should not call Graph")).GetOperatingNumberAsync(Guid.NewGuid(), cancelled.Token));
    }
}