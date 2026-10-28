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

// What an Officer needs to know about a citizen they are about to write a record for.
public sealed record CitizenDto(Guid? AccountId, string? OperatingNumber);
