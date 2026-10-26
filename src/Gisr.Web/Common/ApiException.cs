using System.Net.Http.Headers;
using System.Text.Json;

namespace Gisr.Web.Common;

public sealed class ApiException(
    int status,
    string title,
    string? detail,
    IReadOnlyDictionary<string, string[]> errors,
    IReadOnlyDictionary<string, JsonElement> extensions,
    TimeSpan? retryAfter) : Exception(detail ?? title)
{
    public int Status { get; } = status;

    public string Title { get; } = title;

    public string? Detail { get; } = detail;

    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;

    public IReadOnlyDictionary<string, JsonElement> Extensions { get; } = extensions;

    public TimeSpan? RetryAfter { get; } = retryAfter;

    public bool IsSealed =>
        Extensions.TryGetValue("sealed", out var value) && value.ValueKind == JsonValueKind.True;

    internal static async Task<ApiException> FromResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        var title = response.ReasonPhrase ?? "Request failed";
        string? detail = null;
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var extensions = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        var body = response.Content is null ? string.Empty : await response.Content.ReadAsStringAsync(cancellationToken);
        if (body.Length > 0)
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in document.RootElement.EnumerateObject())
                    {
                        switch (property.Name)
                        {
                            case "title" when property.Value.ValueKind == JsonValueKind.String:
                                title = property.Value.GetString()!;
                                break;
                            case "status" or "type" or "instance" or "traceId":
                                break;
                            case "detail" when property.Value.ValueKind == JsonValueKind.String:
                                detail = property.Value.GetString();
                                break;
                            case "errors" when property.Value.ValueKind == JsonValueKind.Object:
                                foreach (var field in property.Value.EnumerateObject())
                                {
                                    errors[field.Name] = [.. field.Value.EnumerateArray().Select(e => e.GetString() ?? string.Empty)];
                                }

                                break;
                            default:
                                extensions[property.Name] = property.Value.Clone();
                                break;
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Non-JSON error bodies fall back to the status line.
            }
        }

        return new ApiException(status, title, detail, errors, extensions, response.Headers.RetryAfter?.Delta);
    }
}
