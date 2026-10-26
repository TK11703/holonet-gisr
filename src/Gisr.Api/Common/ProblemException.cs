namespace Gisr.Api.Common;

public class ProblemException(
    int status,
    string title,
    string? detail = null,
    IReadOnlyDictionary<string, object?>? extensions = null,
    IReadOnlyDictionary<string, string>? headers = null) : Exception(detail ?? title)
{
    public int Status { get; } = status;

    public string Title { get; } = title;

    public string? Detail { get; } = detail;

    public IReadOnlyDictionary<string, object?>? Extensions { get; } = extensions;

    public IReadOnlyDictionary<string, string>? Headers { get; } = headers;
}

// A 400 for rules that need server-side state, reported with the same shape as FluentValidation failures.
public sealed class ValidationProblemException(string field, string message)
    : ProblemException(StatusCodes.Status400BadRequest, "One or more validation errors occurred.", message)
{
    public string Field { get; } = field;
}
