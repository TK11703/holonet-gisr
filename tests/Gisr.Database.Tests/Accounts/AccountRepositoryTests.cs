using Gisr.Database.Accounts;
using Gisr.Database.Common;
using Gisr.Database.Tests.Common;
using Npgsql;

namespace Gisr.Database.Tests.Accounts;

[Collection("postgres")]
public class AccountRepositoryTests(PostgresFixture postgres)
{
    private static AccountRepository Repository(TestDatabase db) => new(db.DataSource, new SqlScriptLoader());

    [Fact]
    public async Task Upsert_CreatesExactlyOneRowPerTenantAndObject()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var repository = Repository(db);
        var tenant = Guid.NewGuid();
        var oid = Guid.NewGuid();

        var first = await repository.UpsertAsync(tenant, oid, "Luke Skywalker", "luke@rebels.example", CancellationToken.None);
        var second = await repository.UpsertAsync(tenant, oid, "Luke Skywalker", "luke@rebels.example", CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await db.ScalarAsync<int>("SELECT count(*) FROM account"));
    }

    [Fact]
    public async Task Upsert_SameObjectInDifferentTenants_CreatesTwoAccounts()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var repository = Repository(db);
        var oid = Guid.NewGuid();

        var a = await repository.UpsertAsync(Guid.NewGuid(), oid, "A", null, CancellationToken.None);
        var b = await repository.UpsertAsync(Guid.NewGuid(), oid, "B", null, CancellationToken.None);

        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public async Task Upsert_OnSecondSignIn_UpdatesLastSignInAndEmailOnly()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var repository = Repository(db);
        var tenant = Guid.NewGuid();
        var oid = Guid.NewGuid();
        var first = await repository.UpsertAsync(tenant, oid, "Original Name", "old@example.test", CancellationToken.None);
        await repository.UpdateSettingsAsync(first.Id, "Chosen Name", "contact@example.test", false, CancellationToken.None);

        var second = await repository.UpsertAsync(tenant, oid, "Token Name", "new@example.test", CancellationToken.None);

        Assert.Equal("Chosen Name", second.DisplayName);
        Assert.Equal("contact@example.test", second.ContactEmail);
        Assert.False(second.NotifyOnServiceRecord);
        Assert.Equal("new@example.test", second.SignInEmail);
        Assert.Equal(first.CreatedAt, second.CreatedAt);
        Assert.True(second.LastSignInAt >= first.LastSignInAt);
    }

    [Fact]
    public async Task NewAccount_HasDefaults()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();

        var account = await Repository(db).UpsertAsync(Guid.NewGuid(), Guid.NewGuid(), "Han Solo", null, CancellationToken.None);

        Assert.True(account.NotifyOnServiceRecord);
        Assert.Null(account.ContactEmail);
        Assert.Equal("Han Solo", account.DisplayName);
        Assert.Equal(TimeSpan.Zero, account.CreatedAt.Offset);
    }

    [Fact]
    public async Task UpdateSettings_PersistsChanges_AndReturnsUpdatedRow()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var repository = Repository(db);
        var account = await repository.UpsertAsync(Guid.NewGuid(), Guid.NewGuid(), "Old", null, CancellationToken.None);

        var updated = await repository.UpdateSettingsAsync(account.Id, "New", "new@example.test", false, CancellationToken.None);
        var reloaded = await repository.GetAsync(account.Id, CancellationToken.None);

        Assert.Equal("New", updated?.DisplayName);
        Assert.Equal("new@example.test", reloaded?.ContactEmail);
        Assert.False(reloaded?.NotifyOnServiceRecord);
    }

    [Fact]
    public async Task UpdateSettings_ReturnsNull_ForUnknownAccount()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();

        var result = await Repository(db).UpdateSettingsAsync(Guid.NewGuid(), "x", null, true, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Get_ReturnsNull_ForUnknownAccount()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();

        Assert.Null(await Repository(db).GetAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task DisplayName_Over100Characters_IsRejectedByDatabase()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var repository = Repository(db);
        var account = await repository.UpsertAsync(Guid.NewGuid(), Guid.NewGuid(), "Ok", null, CancellationToken.None);

        await Assert.ThrowsAsync<PostgresException>(() =>
            repository.UpdateSettingsAsync(account.Id, new string('x', 101), null, true, CancellationToken.None));
    }
}
