using Gisr.Shared.Accounts;

namespace Gisr.Web.Common;

public sealed class CurrentUserState(ApiClient api)
{
    private Task? _loading;

    public AccountDto? Account { get; private set; }

    public bool IsOfficer => Account?.IsOfficer ?? false;

    public event Action? Changed;

    public Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (Account is not null)
        {
            return Task.CompletedTask;
        }

        return _loading ??= LoadAsync(cancellationToken);
    }

    public void Update(AccountDto account)
    {
        Account = account;
        Changed?.Invoke();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            Account = await api.GetAsync<AccountDto>("api/me", cancellationToken: cancellationToken);
            Changed?.Invoke();
        }
        finally
        {
            // A failed load must be retryable on the next component render.
            _loading = null;
        }
    }
}
