using System.Net.Http.Headers;
using System.Text.Json;

namespace Gisr.Api.Accounts;

public sealed class GraphAccountIdentity(HttpClient http, IAccountIdentityTokenProvider tokens) : IAccountIdentityDirectory
{
    public async Task<string?> GetOperatingNumberAsync(Guid objectId, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "me?$select=id,employeeId");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(cancellationToken));
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) throw Unavailable();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String
                || !Guid.TryParse(id.GetString(), out var returnedId) || returnedId != objectId
                || !root.TryGetProperty("employeeId", out var employeeId)) throw Unavailable();
            if (employeeId.ValueKind == JsonValueKind.Null) return null;
            if (employeeId.ValueKind != JsonValueKind.String) throw Unavailable();
            var number = employeeId.GetString()!.Trim();
            if (number.Length > 64) throw Unavailable();
            return number.Length == 0 ? null : number;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException
            || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw Unavailable();
        }
    }

    private static DirectoryUnavailableException Unavailable() => new("The current user's directory identity is unavailable.");
}