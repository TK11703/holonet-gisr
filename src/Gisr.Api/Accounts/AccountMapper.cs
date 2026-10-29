using Gisr.Database.Accounts;
using Gisr.Shared.Accounts;

namespace Gisr.Api.Accounts;

public static class AccountMapper
{
    public static AccountDto ToDto(AccountRow row, bool isOfficer) =>
        new(row.Id, row.DisplayName, row.SignInEmail, row.ContactEmail, row.NotifyOnServiceRecord, isOfficer, row.CreatedAt, row.OperatingNumber, row.Theme);
}
