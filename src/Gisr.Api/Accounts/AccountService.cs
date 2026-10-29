using Gisr.Api.Common;
using Gisr.Database.Accounts;
using Gisr.Shared.Accounts;

namespace Gisr.Api.Accounts;

public sealed class AccountService(AccountRepository accounts, ITenantDirectory directory, ICurrentUser user, IAccountIdentityDirectory identity)
{
    private const string FallbackDisplayName = "Citizen";
    private const int DefaultSearchPageSize = 10;

    public async Task<AccountRow> EnsureAccountAsync(CancellationToken cancellationToken)
    {
        var displayName = FirstNonBlank(user.DisplayName, user.Email) ?? FallbackDisplayName;
        displayName = Truncate(displayName);

        return await accounts.UpsertAsync(user.TenantId, user.ObjectId, displayName, user.Email, cancellationToken);
    }

    public Task<AccountRow?> FindCitizenAsync(Guid objectId, CancellationToken cancellationToken) =>
        accounts.FindByObjectAsync(user.TenantId, objectId, cancellationToken);

    // Officers pick the subject from the whole tenant, so the person may not have signed in yet.
    public async Task<AccountRow> ResolveSubjectAsync(Guid objectId, CancellationToken cancellationToken)
    {
        var existing = await accounts.FindByObjectAsync(user.TenantId, objectId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var person = await InDirectoryAsync(() => directory.GetAsync(objectId, cancellationToken))
            ?? throw new ProblemException(StatusCodes.Status404NotFound, "Not Found", "The citizen was not found in the tenant directory.");
        var displayName = Truncate(person.DisplayName.Trim());
        return await accounts.EnsurePlaceholderAsync(user.TenantId, objectId, displayName, person.Email, cancellationToken);
    }

    public async Task<IReadOnlyList<DirectoryUserDto>> SearchAsync(AccountSearchQuery query, CancellationToken cancellationToken)
    {
        if (!user.IsOfficer)
        {
            throw new ProblemException(StatusCodes.Status403Forbidden, "Forbidden", "Only Officers can look up other citizens.");
        }

        var term = query.Query!.Trim();
        var pageSize = query.PageSize ?? DefaultSearchPageSize;
        var users = await InDirectoryAsync(() => directory.SearchAsync(term, pageSize, cancellationToken));

        // The directory fields we can read don't include EmployeeId, so Operating Numbers are matched on stored accounts.
        var byNumber = await accounts.SearchByOperatingNumberAsync(user.TenantId, term, pageSize, cancellationToken);
        return
        [
            .. byNumber
                .Select(row => new DirectoryUserDto(row.EntraObjectId, row.DisplayName, row.SignInEmail))
                .Concat(users.Select(found => new DirectoryUserDto(found.ObjectId, found.DisplayName, found.Email)))
                .DistinctBy(found => found.ObjectId)
                .Take(pageSize)
        ];
    }

    private static async Task<T> InDirectoryAsync<T>(Func<Task<T>> lookup)
    {
        try
        {
            return await lookup();
        }
        catch (DirectoryUnavailableException)
        {
            throw new ProblemException(
                StatusCodes.Status503ServiceUnavailable, "Service Unavailable", "The tenant directory is unavailable. Please try again.");
        }
    }

    private static string Truncate(string displayName) =>
        displayName.Length > UpdateAccountSettingsRequestValidator.DisplayNameMaxLength
            ? displayName[..UpdateAccountSettingsRequestValidator.DisplayNameMaxLength]
            : displayName;

    public async Task<AccountDto> GetMeAsync(CancellationToken cancellationToken)
    {
        var account = await EnsureAccountAsync(cancellationToken);
        try
        {
            var number = await identity.GetOperatingNumberAsync(user.ObjectId, cancellationToken);
            account = await accounts.UpdateOperatingNumberAsync(account.Id, number, cancellationToken) ?? account;
        }
        catch (DirectoryUnavailableException)
        {
        }

        return AccountMapper.ToDto(account, user.IsOfficer);
    }

    public async Task<AccountDto> UpdateSettingsAsync(UpdateAccountSettingsRequest request, CancellationToken cancellationToken)
    {
        var account = await EnsureAccountAsync(cancellationToken);
        var contactEmail = string.IsNullOrWhiteSpace(request.ContactEmail) ? null : request.ContactEmail.Trim();

        var updated = await accounts.UpdateSettingsAsync(
            account.Id, request.DisplayName.Trim(), contactEmail, request.NotifyOnServiceRecord, request.Theme, cancellationToken);

        return AccountMapper.ToDto(updated ?? throw new ProblemException(StatusCodes.Status404NotFound, "Not Found"), user.IsOfficer);
    }

    private static string? FirstNonBlank(params string?[] values) =>
        values.Select(v => v?.Trim()).FirstOrDefault(v => !string.IsNullOrEmpty(v));
}
