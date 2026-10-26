namespace Gisr.Shared.Accounts;

public sealed record AccountDto(
    Guid Id,
    string DisplayName,
    string? SignInEmail,
    string? ContactEmail,
    bool NotifyOnServiceRecord,
    bool IsOfficer,
    DateTimeOffset CreatedAt,
    string? OperatingNumber = null);
