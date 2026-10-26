using Gisr.Database.Accounts;
using Gisr.Database.Common;
using Gisr.Database.Tests.Common;

namespace Gisr.Database.Tests.Accounts;

[Collection("postgres")]
public class AccountPlaceholderTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static AccountRepository Repository(TestDatabase db) => new(db.DataSource, new SqlScriptLoader());

    [Fact]
    public async Task FindByObject_ReturnsTheAccountForThatTenantAndObject()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var tenant = Guid.NewGuid();
        var oid = Guid.NewGuid();
        var account = await Repository(db).UpsertAsync(tenant, oid, "Rex", null, Ct);

        var found = await Repository(db).FindByObjectAsync(tenant, oid, Ct);

        Assert.Equal(account.Id, found?.Id);
    }

    [Fact]
    public async Task FindByObject_IsScopedToTheTenant()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var oid = Guid.NewGuid();
        await Repository(db).UpsertAsync(Guid.NewGuid(), oid, "Rex", null, Ct);

        Assert.Null(await Repository(db).FindByObjectAsync(Guid.NewGuid(), oid, Ct));
        Assert.Null(await Repository(db).FindByObjectAsync(Guid.NewGuid(), Guid.NewGuid(), Ct));
    }

    [Fact]
    public async Task Placeholder_IsCreatedFromTheDirectoryEntry_WithoutASignIn()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var tenant = Guid.NewGuid();
        var oid = Guid.NewGuid();

        var account = await Repository(db).EnsurePlaceholderAsync(tenant, oid, "Padme Amidala", "padme@naboo.example", Ct);

        Assert.Equal(tenant, account.TenantId);
        Assert.Equal(oid, account.EntraObjectId);
        Assert.Equal("Padme Amidala", account.DisplayName);
        Assert.Equal("padme@naboo.example", account.SignInEmail);
        Assert.True(account.NotifyOnServiceRecord);
        Assert.Null(account.LastSignInAt);
        Assert.Equal(TimeSpan.Zero, account.CreatedAt.Offset);
    }

    [Fact]
    public async Task Placeholder_IsIdempotent_AndNeverOverwritesAnExistingAccount()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var tenant = Guid.NewGuid();
        var oid = Guid.NewGuid();
        var real = await Repository(db).UpsertAsync(tenant, oid, "Chosen Name", "real@example.test", Ct);

        var again = await Repository(db).EnsurePlaceholderAsync(tenant, oid, "Directory Name", "directory@example.test", Ct);
        var twice = await Repository(db).EnsurePlaceholderAsync(tenant, oid, "Directory Name", "directory@example.test", Ct);

        Assert.Equal(real.Id, again.Id);
        Assert.Equal(real.Id, twice.Id);
        Assert.Equal("Chosen Name", again.DisplayName);
        Assert.NotNull(again.LastSignInAt);
        Assert.Equal(1, await db.ScalarAsync<int>("SELECT count(*) FROM account"));
    }

    [Fact]
    public async Task FirstSignIn_AdoptsThePlaceholder_KeepingItsIdAndName()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var tenant = Guid.NewGuid();
        var oid = Guid.NewGuid();
        var placeholder = await Repository(db).EnsurePlaceholderAsync(tenant, oid, "Directory Name", "dir@example.test", Ct);

        var signedIn = await Repository(db).UpsertAsync(tenant, oid, "Token Name", "token@example.test", Ct);

        Assert.Equal(placeholder.Id, signedIn.Id);
        Assert.Equal("Directory Name", signedIn.DisplayName);
        Assert.Equal("token@example.test", signedIn.SignInEmail);
        Assert.NotNull(signedIn.LastSignInAt);
        Assert.Equal(1, await db.ScalarAsync<int>("SELECT count(*) FROM account"));
    }

    [Fact]
    public async Task Placeholder_WithoutAnEmail_StoresNull()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();

        var account = await Repository(db).EnsurePlaceholderAsync(Guid.NewGuid(), Guid.NewGuid(), "No Mail", null, Ct);

        Assert.Null(account.SignInEmail);
    }
}
