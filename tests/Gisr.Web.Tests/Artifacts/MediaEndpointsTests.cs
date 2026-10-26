using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Gisr.Web.Artifacts;
using Gisr.Web.Records;
using Gisr.Web.Tests.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Gisr.Web.Tests.Artifacts;

public class MediaEndpointsTests
{
    private const string UserOid = "44444444-4444-4444-4444-444444444444";
    private const string Token = "view-token-view-token-view-token-view-token-1234";

    private readonly FakeApi _api = new();
    private readonly ViewGrantStore _grants = new();
    private readonly Guid _artifactId = Guid.NewGuid();

    private static ClaimsPrincipal User(string oid = UserOid) => new(new ClaimsIdentity([new Claim("oid", oid)], "test"));

    private static HttpResponseMessage Bytes(byte[] bytes, string contentType, string? disposition = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        if (disposition is not null)
        {
            response.Content.Headers.TryAddWithoutValidation("Content-Disposition", disposition);
        }

        return response;
    }

    private async Task<(DefaultHttpContext Http, IResult Result)> RunAsync(bool thumbnail = false, Guid? v = null, string oid = UserOid)
    {
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
            User = User(oid),
        };
        http.Response.Body = new MemoryStream();
        var result = await MediaEndpoints.ProxyAsync(_artifactId, thumbnail, v, http, _api.CreateClient(), _grants, CancellationToken.None);
        return (http, result);
    }

    private static async Task<byte[]> BodyAsync(DefaultHttpContext http, IResult result)
    {
        await result.ExecuteAsync(http);
        return ((MemoryStream)http.Response.Body).ToArray();
    }

    [Fact]
    public async Task Content_IsStreamedFromTheApi_WithItsContentType()
    {
        var png = new byte[] { 1, 2, 3, 4, 5 };
        _api.On(HttpMethod.Get, $"api/artifacts/{_artifactId}/content", _ => Bytes(png, "image/png"));

        var (http, result) = await RunAsync();

        Assert.Equal(png, await BodyAsync(http, result));
        Assert.Equal("image/png", http.Response.ContentType);
        Assert.Equal("test-token", _api.Calls.Single().Headers["Authorization"].Replace("Bearer ", string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Thumbnails_AreStreamedFromTheThumbnailEndpoint()
    {
        _api.On(HttpMethod.Get, $"api/artifacts/{_artifactId}/thumbnail", _ => Bytes([9, 9], "image/jpeg"));

        var (http, result) = await RunAsync(thumbnail: true);

        Assert.Equal([9, 9], await BodyAsync(http, result));
        Assert.Equal("image/jpeg", http.Response.ContentType);
    }

    [Fact]
    public async Task EveryResponse_CarriesTheSecurityHeaders_AndAPrivateCachePolicy()
    {
        _api.On(HttpMethod.Get, $"api/artifacts/{_artifactId}/content", _ => Bytes([1], "image/png"));

        var (http, _) = await RunAsync();

        Assert.Equal("nosniff", http.Response.Headers.XContentTypeOptions);
        Assert.Equal("private", http.Response.Headers.CacheControl);
        Assert.Contains("sandbox", http.Response.Headers.ContentSecurityPolicy.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NonImages_AreServedAsAttachments_UsingTheApisFileName()
    {
        _api.On(HttpMethod.Get, $"api/artifacts/{_artifactId}/content", _ =>
            Bytes([1], "application/pdf", "attachment; filename*=UTF-8''orders.pdf"));

        var (http, _) = await RunAsync();

        Assert.Equal("attachment; filename*=UTF-8''orders.pdf", http.Response.Headers.ContentDisposition.ToString());
    }

    [Fact]
    public async Task NonImages_AreAlwaysAttachments_EvenIfTheApiForgotTheHeader()
    {
        _api.On(HttpMethod.Get, $"api/artifacts/{_artifactId}/content", _ => Bytes([1], "text/plain"));

        var (http, _) = await RunAsync();

        Assert.StartsWith("attachment", http.Response.Headers.ContentDisposition.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Images_AreNotAttachments()
    {
        _api.On(HttpMethod.Get, $"api/artifacts/{_artifactId}/content", _ => Bytes([1], "image/png"));

        var (http, _) = await RunAsync();

        Assert.Equal(string.Empty, http.Response.Headers.ContentDisposition.ToString());
    }

    [Fact]
    public async Task AViewingId_AttachesTheServerHeldToken_AndTurnsOffCaching()
    {
        var recordId = Guid.NewGuid();
        var viewingId = _grants.Register(UserOid, recordId, Token);
        _api.On(HttpMethod.Get, $"api/artifacts/{_artifactId}/content", _ => Bytes([1], "image/png"));

        var (http, _) = await RunAsync(v: viewingId);

        Assert.Equal(Token, _api.Calls.Single().Headers["X-View-Token"]);
        Assert.Equal("private, no-store", http.Response.Headers.CacheControl);
    }

    [Fact]
    public async Task AViewingId_AlsoWorksForThumbnails()
    {
        var viewingId = _grants.Register(UserOid, Guid.NewGuid(), Token);
        _api.On(HttpMethod.Get, $"api/artifacts/{_artifactId}/thumbnail", _ => Bytes([1], "image/jpeg"));

        await RunAsync(thumbnail: true, v: viewingId);

        Assert.Equal(Token, _api.Calls.Single().Headers["X-View-Token"]);
    }

    [Fact]
    public async Task AnUnknownViewingId_IsNotFound_WithoutCallingTheApi()
    {
        var (http, result) = await RunAsync(v: Guid.NewGuid());

        await result.ExecuteAsync(http);

        Assert.Equal(StatusCodes.Status404NotFound, http.Response.StatusCode);
        Assert.Empty(_api.Calls);
    }

    [Fact]
    public async Task AnotherUsersViewingId_IsNotFound_WithoutCallingTheApi()
    {
        var viewingId = _grants.Register("someone-else", Guid.NewGuid(), Token);

        var (http, result) = await RunAsync(v: viewingId);
        await result.ExecuteAsync(http);

        Assert.Equal(StatusCodes.Status404NotFound, http.Response.StatusCode);
        Assert.Empty(_api.Calls);
    }

    [Fact]
    public async Task WithoutAViewingId_NoTokenIsSent_SoALockedRecordsFileIsNotFound()
    {
        _api.On(HttpMethod.Get, $"api/artifacts/{_artifactId}/content", call =>
            call.Headers.ContainsKey("X-View-Token") ? Bytes([1], "image/png") : new HttpResponseMessage(HttpStatusCode.NotFound));

        var (http, result) = await RunAsync();
        await result.ExecuteAsync(http);

        Assert.Equal(StatusCodes.Status404NotFound, http.Response.StatusCode);
        Assert.DoesNotContain("X-View-Token", _api.Calls.Single().Headers.Keys);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(HttpStatusCode.Forbidden, StatusCodes.Status403Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError, StatusCodes.Status502BadGateway)]
    [InlineData(HttpStatusCode.TooManyRequests, StatusCodes.Status502BadGateway)]
    public async Task ApiFailures_AreMappedToSafeStatusCodes(HttpStatusCode api, int expected)
    {
        _api.On(HttpMethod.Get, $"api/artifacts/{_artifactId}/content", _ => FakeApi.Problem(api, new { status = (int)api, detail = "internal detail" }));

        var (http, result) = await RunAsync();
        await result.ExecuteAsync(http);

        Assert.Equal(expected, http.Response.StatusCode);
        Assert.DoesNotContain("internal detail", System.Text.Encoding.UTF8.GetString(((MemoryStream)http.Response.Body).ToArray()), StringComparison.Ordinal);
    }

    [Fact]
    public void TheEndpoints_AreMapped_AndRequireAnAuthenticatedUser()
    {
        var builder = WebApplication.CreateBuilder();
        var app = builder.Build();

        app.MapMediaEndpoints();

        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints).OfType<RouteEndpoint>().ToList();
        Assert.Contains(endpoints, e => e.RoutePattern.RawText == "/media/artifacts/{id:guid}");
        Assert.Contains(endpoints, e => e.RoutePattern.RawText == "/media/artifacts/{id:guid}/thumbnail");
        Assert.All(endpoints, e => Assert.NotEmpty(e.Metadata.GetOrderedMetadata<IAuthorizeData>()));
    }
}
