namespace Gisr.Api.Accounts;

public sealed record DirectoryUser(Guid ObjectId, string DisplayName, string? Email);

// Read-only view of the Entra tenant directory, used to pick the subject of a service record.
public interface ITenantDirectory
{
    Task<IReadOnlyList<DirectoryUser>> SearchAsync(string query, int pageSize, CancellationToken cancellationToken);

    Task<DirectoryUser?> GetAsync(Guid objectId, CancellationToken cancellationToken);
}

public interface IGraphTokenProvider
{
    Task<string> GetTokenAsync(CancellationToken cancellationToken);
}

public interface IAccountIdentityDirectory
{
    Task<string?> GetOperatingNumberAsync(Guid objectId, CancellationToken cancellationToken);
}

public interface IAccountIdentityTokenProvider
{
    Task<string> GetTokenAsync(CancellationToken cancellationToken);
}

public sealed class DirectoryUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
