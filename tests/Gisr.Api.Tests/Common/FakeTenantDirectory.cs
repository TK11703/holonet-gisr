using Gisr.Api.Accounts;

namespace Gisr.Api.Tests.Common;

public sealed class FakeTenantDirectory : ITenantDirectory
{
    private readonly List<DirectoryUser> _users = [];
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    public bool Unavailable { get; set; }

    public Guid Add(string displayName, string? email = null, Guid? objectId = null)
    {
        var id = objectId ?? Guid.NewGuid();
        _users.Add(new DirectoryUser(id, displayName, email));
        return id;
    }

    public Task<IReadOnlyList<DirectoryUser>> SearchAsync(string query, int pageSize, CancellationToken cancellationToken)
    {
        Touch();
        IReadOnlyList<DirectoryUser> found = [.. _users
            .Where(u => u.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || (u.Email?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderBy(u => u.DisplayName, StringComparer.Ordinal)
            .Take(pageSize)];
        return Task.FromResult(found);
    }

    public Task<DirectoryUser?> GetAsync(Guid objectId, CancellationToken cancellationToken)
    {
        Touch();
        return Task.FromResult(_users.FirstOrDefault(u => u.ObjectId == objectId));
    }

    private void Touch()
    {
        Interlocked.Increment(ref _calls);
        if (Unavailable)
        {
            throw new DirectoryUnavailableException("The directory is down.");
        }
    }
}
