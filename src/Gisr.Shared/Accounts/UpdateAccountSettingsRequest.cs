namespace Gisr.Shared.Accounts;

public sealed record UpdateAccountSettingsRequest(string DisplayName, string? ContactEmail, bool NotifyOnServiceRecord);
