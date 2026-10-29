using Azure.Core;
using Gisr.Database.Common;
using Npgsql;

namespace Gisr.Database.Tests.Common;

[Collection("postgres")]
public class GisrDataSourceTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Create_WithCredential_SendsTheEntraTokenAsThePassword()
    {
        // The container's password stands in for an Entra token: it only connects if the provider supplied it.
        var connection = new NpgsqlConnectionStringBuilder(postgres.ConnectionString);
        var token = connection.Password!;
        connection.Password = null;
        var credential = new FixedTokenCredential(token);

        await using var dataSource = GisrDataSource.Create(connection.ConnectionString, credential);
        await using var command = dataSource.CreateCommand("SELECT 1");

        Assert.Equal(1, await command.ExecuteScalarAsync());
        Assert.Equal([GisrDataSource.EntraScope], credential.RequestedScopes);
    }

    private sealed class FixedTokenCredential(string token) : TokenCredential
    {
        public List<string> RequestedScopes { get; } = [];

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            Issue(requestContext);

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Issue(requestContext));

        private AccessToken Issue(TokenRequestContext requestContext)
        {
            RequestedScopes.AddRange(requestContext.Scopes);
            return new AccessToken(token, DateTimeOffset.UtcNow.AddHours(1));
        }
    }
}
