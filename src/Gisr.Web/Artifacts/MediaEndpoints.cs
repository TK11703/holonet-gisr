using Gisr.Web.Common;
using Gisr.Web.Records;
using Microsoft.AspNetCore.Mvc;

namespace Gisr.Web.Artifacts;

// The browser never talks to the Api: bytes stream through here with the user's token, and a view token only ever travels server-side.
public static class MediaEndpoints
{
    private const string ViewTokenHeader = "X-View-Token";

    public static IEndpointRouteBuilder MapMediaEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/media/artifacts").RequireAuthorization();

        group.MapGet("/{id:guid}", (Guid id, Guid? v, HttpContext http, [FromServices] ApiClient api, [FromServices] ViewGrantStore grants, CancellationToken cancellationToken) =>
            ProxyAsync(id, thumbnail: false, v, http, api, grants, cancellationToken));
        group.MapGet("/{id:guid}/thumbnail", (Guid id, Guid? v, HttpContext http, [FromServices] ApiClient api, [FromServices] ViewGrantStore grants, CancellationToken cancellationToken) =>
            ProxyAsync(id, thumbnail: true, v, http, api, grants, cancellationToken));

        return endpoints;
    }

    public static async Task<IResult> ProxyAsync(
        Guid id,
        bool thumbnail,
        Guid? viewingId,
        HttpContext http,
        ApiClient api,
        ViewGrantStore grants,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string>? headers = null;
        if (viewingId is { } viewing)
        {
            // Unknown, expired, or someone else's viewing id never reaches the Api.
            if (!grants.TryGet(ViewGrantStore.UserKey(http.User), viewing, out var entry))
            {
                return Results.NotFound();
            }

            headers = new Dictionary<string, string>(StringComparer.Ordinal) { [ViewTokenHeader] = entry.Token };
        }

        HttpResponseMessage response;
        try
        {
            response = await api.OpenStreamAsync($"api/artifacts/{id}/{(thumbnail ? "thumbnail" : "content")}", headers, cancellationToken);
        }
        catch (ApiException ex)
        {
            return Results.StatusCode(ex.Status is StatusCodes.Status404NotFound or StatusCodes.Status403Forbidden ? ex.Status : StatusCodes.Status502BadGateway);
        }
        catch (HttpRequestException)
        {
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }

        http.Response.RegisterForDispose(response);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        var responseHeaders = http.Response.Headers;
        responseHeaders.XContentTypeOptions = "nosniff";
        responseHeaders.ContentSecurityPolicy = "default-src 'none'; sandbox";
        responseHeaders.CacheControl = viewingId is null ? "private" : "private, no-store";
        if (!contentType.StartsWith("image/", StringComparison.Ordinal))
        {
            responseHeaders.ContentDisposition = response.Content.Headers.TryGetValues("Content-Disposition", out var disposition)
                ? disposition.First()
                : "attachment";
        }

        return Results.Stream(await response.Content.ReadAsStreamAsync(cancellationToken), contentType);
    }
}
