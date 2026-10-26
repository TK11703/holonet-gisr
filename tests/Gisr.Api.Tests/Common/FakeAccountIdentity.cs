using Gisr.Api.Accounts;

namespace Gisr.Api.Tests.Common;

public sealed class FakeAccountIdentity : IAccountIdentityDirectory
{
    public Dictionary<Guid, string?> Numbers { get; } = [];
    public bool Unavailable { get; set; }
    public int Calls { get; private set; }

    public Task<string?> GetOperatingNumberAsync(Guid objectId, CancellationToken cancellationToken)
    {
        Calls++;
        cancellationToken.ThrowIfCancellationRequested();
        if (Unavailable) throw new DirectoryUnavailableException("Directory unavailable");
        return Task.FromResult(Numbers.GetValueOrDefault(objectId));
    }
}