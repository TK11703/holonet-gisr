using Microsoft.Identity.Client;
using Microsoft.Identity.Web;

namespace Gisr.Api.Accounts;

// On-behalf-of: Graph is called as the signed-in Officer with the least-privilege User.ReadBasic.All scope.
public sealed class MicrosoftIdentityGraphTokenProvider(ITokenAcquisition tokenAcquisition) : IGraphTokenProvider
{
    private static readonly string[] Scopes = ["https://graph.microsoft.com/User.ReadBasic.All"];

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await tokenAcquisition.GetAccessTokenForUserAsync(Scopes);
        }
        catch (Exception ex) when (ex is MicrosoftIdentityWebChallengeUserException or MsalException or InvalidOperationException)
        {
            throw new DirectoryUnavailableException("A Graph token could not be acquired for the signed-in user.", ex);
        }
    }
}
