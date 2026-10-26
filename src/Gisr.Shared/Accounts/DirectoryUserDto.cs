namespace Gisr.Shared.Accounts;

// A person from the Entra tenant directory; ObjectId is the Entra object id, not a GISR account id.
public sealed record DirectoryUserDto(Guid ObjectId, string DisplayName, string? Email);

