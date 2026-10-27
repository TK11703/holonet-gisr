using System.Text;
using Gisr.Api.Common;
using Gisr.Database.Accounts;
using Gisr.Shared.Accounts;

namespace Gisr.Api.Accounts;

public sealed class AccountAffiliationService(AccountAffiliationRepository affiliations, AccountService accounts, ICurrentUser user)
{
    public async Task<IReadOnlyList<AccountAffiliationDto>> ListAsync(CancellationToken cancellationToken)
    {
        var account = await accounts.EnsureAccountAsync(cancellationToken);
        return (await affiliations.ListAsync(account.Id, cancellationToken)).Select(ToDto).ToList();
    }

    // Officers pick a citizen to write about; a citizen who never signed in has no account yet and so no affiliations.
    public async Task<IReadOnlyList<AccountAffiliationDto>> ListForCitizenAsync(Guid? accountId, Guid? objectId, CancellationToken cancellationToken)
    {
        if (!user.IsOfficer)
        {
            throw new ProblemException(StatusCodes.Status403Forbidden, "Forbidden", "Only Officers can look up another citizen's affiliations.");
        }

        var id = accountId ?? (await accounts.FindCitizenAsync(objectId!.Value, cancellationToken))?.Id;
        return id is null ? [] : (await affiliations.ListAsync(id.Value, cancellationToken)).Select(ToDto).ToList();
    }

    public async Task<AccountAffiliationDto> AddAsync(AddAccountAffiliationRequest request, CancellationToken cancellationToken)
    {
        var account = await accounts.EnsureAccountAsync(cancellationToken);
        var result = await affiliations.AddAsync(
            account.Id, Text(request.Primary)!, Text(request.Secondary), Text(request.SpecialUnit),
            Text(request.Prefix)?.ToUpperInvariant(), Text(request.OperatingNumber)?.ToUpperInvariant(), cancellationToken);
        return result.Status switch
        {
            AccountAffiliationAddStatus.Added => ToDto(result.Row!),
            AccountAffiliationAddStatus.Duplicate => throw new ConflictException("This affiliation has already been added."),
            _ => throw new ConflictException($"You can associate at most {AccountAffiliationRepository.MaxPerAccount} affiliations."),
        };
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var account = await accounts.EnsureAccountAsync(cancellationToken);
        if (!await affiliations.DeleteAsync(account.Id, id, cancellationToken))
        {
            throw new ProblemException(StatusCodes.Status404NotFound, "Not Found", "The affiliation was not found.");
        }
    }

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Normalize(NormalizationForm.FormC);

    private static AccountAffiliationDto ToDto(AccountAffiliationRow row) =>
        new(row.Id, row.PrimaryAffiliation, row.SecondaryAffiliation, row.SpecialUnit, row.Prefix, row.OperatingNumber, row.CreatedAt);
}
