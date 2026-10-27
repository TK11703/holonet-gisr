using Gisr.Database.Accounts;
using Gisr.Database.Common;
using Gisr.Database.Tests.Common;

namespace Gisr.Database.Tests.Accounts;

[Collection("postgres")]
public class AccountAffiliationRepositoryTests(PostgresFixture postgres)
{
    [Fact]
    public async Task AddListDelete_AreOwnerScoped_AndRejectDuplicates()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var accounts = new AccountRepository(db.DataSource, new SqlScriptLoader());
        var owner = await accounts.UpsertAsync(Guid.NewGuid(), Guid.NewGuid(), "Citizen", null, CancellationToken.None);
        var other = await accounts.UpsertAsync(Guid.NewGuid(), Guid.NewGuid(), "Other", null, CancellationToken.None);
        var repository = new AccountAffiliationRepository(db.DataSource, new SqlScriptLoader());

        var added = await repository.AddAsync(owner.Id, "Galactic Empire", "Stormtrooper Corps", "501st Legion", "TK", "11703", CancellationToken.None);
        Assert.Equal(AccountAffiliationAddStatus.Added, added.Status);
        Assert.Equal(TimeSpan.Zero, added.Row!.CreatedAt.Offset);
        Assert.Equal(AccountAffiliationAddStatus.Duplicate,
            (await repository.AddAsync(owner.Id, "Galactic Empire", "Stormtrooper Corps", "501st Legion", null, null, CancellationToken.None)).Status);
        Assert.Equal(AccountAffiliationAddStatus.Added, (await repository.AddAsync(owner.Id, "Galactic Empire", null, null, null, null, CancellationToken.None)).Status);
        Assert.Equal(AccountAffiliationAddStatus.Duplicate, (await repository.AddAsync(owner.Id, "Galactic Empire", null, null, null, null, CancellationToken.None)).Status);
        Assert.Equal(AccountAffiliationAddStatus.Added, (await repository.AddAsync(other.Id, "Galactic Empire", null, null, null, null, CancellationToken.None)).Status);

        Assert.Equal(2, (await repository.ListAsync(owner.Id, CancellationToken.None)).Count);
        Assert.False(await repository.DeleteAsync(other.Id, added.Row.Id, CancellationToken.None));
        Assert.True(await repository.DeleteAsync(owner.Id, added.Row.Id, CancellationToken.None));
        Assert.Single(await repository.ListAsync(owner.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Add_StopsAtLimit_AndAccountDeletionCascades()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var accounts = new AccountRepository(db.DataSource, new SqlScriptLoader());
        var owner = await accounts.UpsertAsync(Guid.NewGuid(), Guid.NewGuid(), "Citizen", null, CancellationToken.None);
        var repository = new AccountAffiliationRepository(db.DataSource, new SqlScriptLoader());
        for (var index = 0; index < AccountAffiliationRepository.MaxPerAccount; index++)
        {
            Assert.Equal(AccountAffiliationAddStatus.Added, (await repository.AddAsync(owner.Id, "Galactic Empire", $"Unit {index}", null, null, null, CancellationToken.None)).Status);
        }

        Assert.Equal(AccountAffiliationAddStatus.LimitReached, (await repository.AddAsync(owner.Id, "Galactic Empire", "Extra", null, null, null, CancellationToken.None)).Status);
        await db.ExecuteAsync($"DELETE FROM account WHERE id = '{owner.Id}'");
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT count(*) FROM account_affiliation"));
    }
}
