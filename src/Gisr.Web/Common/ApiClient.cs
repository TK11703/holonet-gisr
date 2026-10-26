using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Gisr.Web.Common;

public interface IAccessTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken);
}

public sealed class ApiClient(HttpClient http, IAccessTokenProvider tokens)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<T?> GetAsync<T>(string path, IReadOnlyDictionary<string, string>? headers = null, CancellationToken cancellationToken = default) =>
        SendAsync<T>(HttpMethod.Get, path, null, headers, cancellationToken);

    public Task<T?> PostAsync<T>(string path, object? body = null, IReadOnlyDictionary<string, string>? headers = null, CancellationToken cancellationToken = default) =>
        SendAsync<T>(HttpMethod.Post, path, body, headers, cancellationToken);

    public Task<T?> PutAsync<T>(string path, object? body, IReadOnlyDictionary<string, string>? headers = null, CancellationToken cancellationToken = default) =>
        SendAsync<T>(HttpMethod.Put, path, body, headers, cancellationToken);

    public Task<T?> PatchAsync<T>(string path, object? body, IReadOnlyDictionary<string, string>? headers = null, CancellationToken cancellationToken = default) =>
        SendAsync<T>(HttpMethod.Patch, path, body, headers, cancellationToken);

    public async Task SendAsync(HttpMethod method, string path, object? body = null, IReadOnlyDictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
    {
        using var response = await SendRawAsync(method, path, body, headers, HttpCompletionOption.ResponseContentRead, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<T?> UploadAsync<T>(
        string path,
        string fileName,
        Stream content,
        IReadOnlyDictionary<string, string>? fields = null,
        CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        var file = new StreamContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        foreach (var (name, value) in fields ?? new Dictionary<string, string>())
        {
            form.Add(new StringContent(value), name);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetAccessTokenAsync(cancellationToken));
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return response.StatusCode == HttpStatusCode.NoContent ? default : await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken);
    }

    // Caller owns and disposes the response; used to stream artifact bytes through the Web proxy.
    public async Task<HttpResponseMessage> OpenStreamAsync(string path, IReadOnlyDictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
    {
        var response = await SendRawAsync(HttpMethod.Get, path, null, headers, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            try
            {
                throw await ApiException.FromResponseAsync(response, cancellationToken);
            }
            finally
            {
                response.Dispose();
            }
        }

        return response;
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body, IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
    {
        using var response = await SendRawAsync(method, path, body, headers, HttpCompletionOption.ResponseContentRead, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0)
        {
            return default;
        }

        return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendRawAsync(
        HttpMethod method,
        string path,
        object? body,
        IReadOnlyDictionary<string, string>? headers,
        HttpCompletionOption completion,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetAccessTokenAsync(cancellationToken));

        if (headers is not null)
        {
            foreach (var (name, value) in headers)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: Json);
        }

        return await http.SendAsync(request, completion, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiException.FromResponseAsync(response, cancellationToken);
        }
    }
}
