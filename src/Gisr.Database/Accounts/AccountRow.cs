namespace Gisr.Database.Accounts;

public sealed class AccountRow
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid EntraObjectId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string? SignInEmail { get; set; }

    public string? ContactEmail { get; set; }

    public bool NotifyOnServiceRecord { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastSignInAt { get; set; }
}
