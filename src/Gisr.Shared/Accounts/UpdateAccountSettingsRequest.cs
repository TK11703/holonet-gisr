namespace Gisr.Shared.Accounts;

// A null Theme leaves the saved theme unchanged.
public sealed record UpdateAccountSettingsRequest(string DisplayName, string? ContactEmail, bool NotifyOnServiceRecord, string? Theme = null);
