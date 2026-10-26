using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Gisr.Api.Accounts;

public sealed class GraphTenantDirectory(HttpClient http, IGraphTokenProvider tokens) : ITenantDirectory
{
    private const string Select = "id,displayName,mail,userPrincipalName";

    public async Task<IReadOnlyList<DirectoryUser>> SearchAsync(string query, int pageSize, CancellationToken cancellationToken)
    {
        var term = Sanitize(query);
        var search = $"\"displayName:{term}\" OR \"mail:{term}\" OR \"userPrincipalName:{term}\"";
        var path = $"users?$search={Uri.EscapeDataString(search)}&$select={Select}&$top={pageSize}";

        using var response = await SendAsync(path, true, cancellationToken);
        EnsureSuccess(response);

        using var document = await ReadAsync(response, cancellationToken);
        var users = new List<DirectoryUser>();
        if (document.RootElement.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array)
        {
            users.AddRange(value.EnumerateArray().Select(Parse).OfType<DirectoryUser>());
        }

        return [.. users.OrderBy(u => u.DisplayName, StringComparer.OrdinalIgnoreCase)];
    }

    public async Task<DirectoryUser?> GetAsync(Guid objectId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync($"users/{objectId}?$select={Select}", false, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        EnsureSuccess(response);
        using var document = await ReadAsync(response, cancellationToken);
        return Parse(document.RootElement);
    }

    // Quotes and backslashes would end the quoted $search term early.
    private static string Sanitize(string query) =>
        string.Concat(query.Where(c => c is not '"' and not '\\' && !char.IsControl(c))).Trim();

    private async Task<HttpResponseMessage> SendAsync(string path, bool eventualConsistency, CancellationToken cancellationToken)
    {
        var token = await tokens.GetTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (eventualConsistency)
        {
            request.Headers.Add("ConsistencyLevel", "eventual");
        }

        try
        {
            return await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new DirectoryUnavailableException("The tenant directory could not be reached.", ex);
        }
    }

    // Response bodies can contain tenant details, so only the status code is reported.
    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new DirectoryUnavailableException($"The tenant directory returned status {(int)response.StatusCode}.");
        }
    }

    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new DirectoryUnavailableException("The tenant directory returned an unreadable response.", ex);
        }
    }

    private static DirectoryUser? Parse(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("id", out var id) || !Guid.TryParse(id.GetString(), out var objectId)
            || !element.TryGetProperty("displayName", out var name) || string.IsNullOrWhiteSpace(name.GetString()))
        {
            return null;
        }

        var email = Text(element, "mail") ?? Text(element, "userPrincipalName");
        return new DirectoryUser(objectId, name.GetString()!, email);
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;
}
