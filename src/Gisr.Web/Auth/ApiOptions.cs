namespace Gisr.Web.Auth;

public sealed class ApiOptions
{
    public const string SectionName = "Api";

    public string BaseUrl { get; set; } = string.Empty;

    public string Scope { get; set; } = string.Empty;
}
