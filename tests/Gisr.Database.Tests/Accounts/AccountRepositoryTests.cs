using Gisr.Database.Accounts;
using Gisr.Database.Common;
using Gisr.Database.Tests.Common;
using Npgsql;

namespace Gisr.Database.Tests.Accounts;

[Collection("postgres")]
public class AccountRepositoryTests(PostgresFixture postgres)
{
    [Fact]
    public async Task OperatingNumber_PersistsAcrossAllAccountProjections_AndCanBeCleared()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var repository = Repository(db);
        var tenant = Guid.NewGuid();
        var oid = Guid.NewGuid();
        var account = await repository.UpsertAsync(tenant, oid, "Citizen", null, CancellationToken.None);
        Assert.Null(account.OperatingNumber);
        Assert.Equal("CT-7567", (await repository.UpdateOperatingNumberAsync(account.Id, "CT-7567", CancellationToken.None))?.OperatingNumber);
        Assert.Equal("CT-7567", (await repository.GetAsync(account.Id, CancellationToken.None))?.OperatingNumber);
        Assert.Equal("CT-7567", (await repository.FindByObjectAsync(tenant, oid, CancellationToken.None))?.OperatingNumber);
        Assert.Equal("CT-7567", (await repository.UpsertAsync(tenant, oid, "Citizen", null, CancellationToken.None)).OperatingNumber);
        Assert.Equal("CT-7567", (await repository.EnsurePlaceholderAsync(tenant, oid, "Citizen", null, CancellationToken.None)).OperatingNumber);
        Assert.Equal("CT-7567", (await repository.UpdateSettingsAsync(account.Id, "Chosen", null, false, CancellationToken.None))?.OperatingNumber);
        Assert.Null((await repository.UpdateOperatingNumberAsync(account.Id, null, CancellationToken.None))?.OperatingNumber);
    }

    [Fact]
    public async Task SearchByOperatingNumber_MatchesPrefixIgnoringCase_WithinTheTenant_TreatingWildcardsLiterally()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var repository = Repository(db);
        var tenant = Guid.NewGuid();
        var rex = await repository.UpsertAsync(tenant, Guid.NewGuid(), "Rex", null, CancellationToken.None);
        await repository.UpdateOperatingNumberAsync(rex.Id, "CT-7567", CancellationToken.None);
        var foreign = await repository.UpsertAsync(Guid.NewGuid(), Guid.NewGuid(), "Foreign", null, CancellationToken.None);
        await repository.UpdateOperatingNumberAsync(foreign.Id, "CT-7568", CancellationToken.None);
        await repository.UpsertAsync(tenant, Guid.NewGuid(), "No Number", null, CancellationToken.None);

        Assert.Equal(["Rex"], (await repository.SearchByOperatingNumberAsync(tenant, "ct-75", 10, CancellationToken.None)).Select(a => a.DisplayName));
        Assert.Empty(await repository.SearchByOperatingNumberAsync(tenant, "7567", 10, CancellationToken.None));
        Assert.Empty(await repository.SearchByOperatingNumberAsync(tenant, "%", 10, CancellationToken.None));
    }

    [Fact]
    public async Task ChainCode_OptionalAtomicReplacementAndDeletion_AreOwnerScoped()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var owner = await Repository(db).UpsertAsync(Guid.NewGuid(), Guid.NewGuid(), "Citizen", null, CancellationToken.None);
        var profiles = new ChainCodeRepository(db.DataSource, new SqlScriptLoader());
        var other = Guid.NewGuid();
        Assert.Null(await profiles.GetAsync(owner.Id, CancellationToken.None));
        var first = await profiles.UpsertAsync(owner.Id, "{\"fullName\":\"Citizen\"}", "12345", CancellationToken.None);
        Assert.Equal(TimeSpan.Zero, first.UpdatedAt.Offset);
        Assert.Equal("12345", (await profiles.GetAsync(owner.Id, CancellationToken.None))?.ChainCode);
        Assert.Null(await profiles.GetAsync(other, CancellationToken.None));
        Assert.False(await profiles.DeleteAsync(other, CancellationToken.None));
        await profiles.UpsertAsync(owner.Id, "{\"homeworld\":\"Naboo\"}", "54321", CancellationToken.None);
        var reloaded = await profiles.GetAsync(owner.Id, CancellationToken.None);
        Assert.Equal("54321", reloaded?.ChainCode);
        Assert.DoesNotContain("fullName", reloaded!.ProfileJson, StringComparison.Ordinal);
        Assert.Equal(1, await db.ScalarAsync<int>("SELECT count(*) FROM account_chain_code"));
        Assert.True(await profiles.DeleteAsync(owner.Id, CancellationToken.None));
        Assert.Null(await profiles.GetAsync(owner.Id, CancellationToken.None));
        Assert.NotNull(await Repository(db).GetAsync(owner.Id, CancellationToken.None));
    }

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
