using System.Security.Claims;

namespace Gisr.Api.Common;

public interface ICurrentUser
{
    Guid TenantId { get; }

    Guid ObjectId { get; }

    bool IsOfficer { get; }

    string? DisplayName { get; }

    string? Email { get; }
}

public sealed class CurrentUser : ICurrentUser
{
    public const string OfficerRole = "Officer";

    private CurrentUser()
    {
    }

    public Guid TenantId { get; private init; }

    public Guid ObjectId { get; private init; }

    public bool IsOfficer { get; private init; }

    public string? DisplayName { get; private init; }

    public string? Email { get; private init; }

    public static CurrentUser From(ClaimsPrincipal principal)
    {
        var objectId = ReadGuid(principal, "oid", "http://schemas.microsoft.com/identity/claims/objectidentifier");
        var tenantId = ReadGuid(principal, "tid", "http://schemas.microsoft.com/identity/claims/tenantid");

        var isOfficer = principal.Claims
            .Where(c => c.Type is "roles" or ClaimTypes.Role)
            .Any(c => string.Equals(c.Value, OfficerRole, StringComparison.Ordinal));

        return new CurrentUser
        {
            ObjectId = objectId,
            TenantId = tenantId,
            IsOfficer = isOfficer,
            DisplayName = principal.FindFirstValue("name") ?? principal.FindFirstValue(ClaimTypes.Name),
            Email = principal.FindFirstValue("preferred_username")
                    ?? principal.FindFirstValue("email")
                    ?? principal.FindFirstValue(ClaimTypes.Email),
        };
    }

    private static Guid ReadGuid(ClaimsPrincipal principal, params string[] claimTypes)
    {
        foreach (var type in claimTypes)
        {
            if (Guid.TryParse(principal.FindFirstValue(type), out var value))
            {
                return value;
            }
        }

        throw new ProblemException(
            StatusCodes.Status403Forbidden,
            "Forbidden",
            "The access token does not identify a user.");
    }
}
