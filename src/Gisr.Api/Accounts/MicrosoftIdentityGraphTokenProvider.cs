using Microsoft.Identity.Client;
using Microsoft.Identity.Web;

namespace Gisr.Api.Accounts;

// On-behalf-of: Graph is called as the signed-in Officer with the least-privilege User.ReadBasic.All scope.
public sealed class MicrosoftIdentityGraphTokenProvider(ITokenAcquisition tokenAcquisition) : IGraphTokenProvider, IAccountIdentityTokenProvider
{
    private static readonly string[] Scopes = ["https://graph.microsoft.com/User.ReadBasic.All"];

    public Task<string> GetTokenAsync(CancellationToken cancellationToken) => AcquireAsync(Scopes, cancellationToken);

    Task<string> IAccountIdentityTokenProvider.GetTokenAsync(CancellationToken cancellationToken) =>
        AcquireAsync(["https://graph.microsoft.com/User.Read"], cancellationToken);

    private async Task<string> AcquireAsync(string[] scopes, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await tokenAcquisition.GetAccessTokenForUserAsync(scopes).WaitAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is MicrosoftIdentityWebChallengeUserException or MsalException or InvalidOperationException)
        {
            throw new DirectoryUnavailableException("A Graph token could not be acquired for the signed-in user.", ex);
        }
    }
}
