namespace Gisr.Shared.Accounts;

public sealed record AccountAffiliationDto(
    Guid Id,
    string Primary,
    string? Secondary,
    string? SpecialUnit,
    string? Prefix,
    string? OperatingNumber,
    DateTimeOffset CreatedAt);

public sealed record AddAccountAffiliationRequest(
    string Primary,
    string? Secondary = null,
    string? SpecialUnit = null,
    string? Prefix = null,
    string? OperatingNumber = null);

public sealed record AffiliationOptionGroupDto(string Category, IReadOnlyList<string> Names);
