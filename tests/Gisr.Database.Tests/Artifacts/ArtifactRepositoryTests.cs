using Gisr.Database.Accounts;
using Gisr.Database.Artifacts;
using Gisr.Database.Common;
using Gisr.Database.Records;
using Gisr.Database.Tests.Common;
using Gisr.Database.Tests.Records;

namespace Gisr.Database.Tests.Artifacts;

internal static class ArtifactTestData
{
    public static ArtifactRepository Artifacts(TestDatabase db) => new(db.DataSource, new SqlScriptLoader());

    public static ArtifactWrite Write(Guid recordId, Guid uploaderId, string fileName = "report.pdf", bool isImage = false, string? description = null, long size = 1024) =>
        new(Guid.CreateVersion7(), recordId, fileName, isImage ? "image/png" : "application/pdf", size, "sha256-" + fileName,
            $"{recordId}/{Guid.NewGuid()}", isImage ? $"{recordId}/{Guid.NewGuid()}.thumb.jpg" : null, isImage, isImage ? description ?? "An image" : description, uploaderId);

    public static async Task<ArtifactRow> AddAsync(TestDatabase db, Guid recordId, Guid uploaderId, string fileName = "report.pdf", bool isImage = false, string? description = null) =>
        await Artifacts(db).InsertAsync(Write(recordId, uploaderId, fileName, isImage, description), CancellationToken.None)
            ?? throw new InvalidOperationException("The artifact limit was reached.");

    public static async Task<(AccountRow Subject, AccountRow Officer, RecordAggregate Record)> SealedServiceRecordAsync(TestDatabase db)
    {
        var subject = await RecordTestData.CreateAccountAsync(db, "Citizen");
        var officer = await RecordTestData.CreateAccountAsync(db, "Officer");
        var record = await RecordTestData.Records(db).CreateAsync(
            subject.Id, officer.Id, RecordTestData.Write() with { IsSealed = true, OverrideCodeHash = "pbkdf2-sha256$1$salt$hash" }, CancellationToken.None);
        return (subject, officer, record);
    }
}

[Collection("postgres")]
public class ArtifactRepositoryTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Insert_ReturnsTheStoredRow_WithTheUploaderName()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var account = await RecordTestData.CreateAccountAsync(db, "Uploader");
        var record = await RecordTestData.Records(db).CreateAsync(account.Id, account.Id, RecordTestData.Write(), CancellationToken.None);
        var write = ArtifactTestData.Write(record.Record.Id, account.Id, "orders.pdf");

        var row = await ArtifactTestData.Artifacts(db).InsertAsync(write, CancellationToken.None);

        Assert.NotNull(row);
        Assert.Equal(write.Id, row.Id);
        Assert.Equal(record.Record.Id, row.RecordId);
        Assert.Equal("orders.pdf", row.FileName);
        Assert.Equal("application/pdf", row.ContentType);
        Assert.Equal(1024, row.SizeBytes);
        Assert.Equal(write.BlobName, row.BlobName);
        Assert.Equal("Uploader", row.UploadedByDisplayName);
        Assert.Equal("pending", row.IndexStatus);
        Assert.Null(row.RemovedAt);
    }

    [Fact]
    public async Task Insert_MarksImagesAsNotSearchable()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var account = await RecordTestData.CreateAccountAsync(db);
        var record = await RecordTestData.Records(db).CreateAsync(account.Id, account.Id, RecordTestData.Write(), CancellationToken.None);

        var row = await ArtifactTestData.AddAsync(db, record.Record.Id, account.Id, "map.png", isImage: true, description: "A star map");

        Assert.True(row.IsImage);
        Assert.Equal("A star map", row.Description);
        Assert.NotNull(row.ThumbnailBlobName);
        Assert.Equal("skipped", row.IndexStatus);
        Assert.Equal("unsupported_type", row.IndexDetail);
    }

    [Fact]
    public async Task Insert_RefusesThe51stActiveArtifact_ButCountsOnlyActiveOnes()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var account = await RecordTestData.CreateAccountAsync(db);
        var record = await RecordTestData.Records(db).CreateAsync(account.Id, account.Id, RecordTestData.Write(), CancellationToken.None);
        var repository = ArtifactTestData.Artifacts(db);
        var first = await ArtifactTestData.AddAsync(db, record.Record.Id, account.Id, "f0.pdf");
        for (var i = 1; i < ArtifactRepository.MaxActivePerRecord; i++)
        {
            await ArtifactTestData.AddAsync(db, record.Record.Id, account.Id, $"f{i}.pdf");
        }

        Assert.Equal(50, await repository.CountActiveAsync(record.Record.Id, CancellationToken.None));
        Assert.Null(await repository.InsertAsync(ArtifactTestData.Write(record.Record.Id, account.Id), CancellationToken.None));

        Assert.True(await repository.RemoveAsync(first.Id, account.Id, CancellationToken.None));

        Assert.Equal(49, await repository.CountActiveAsync(record.Record.Id, CancellationToken.None));
        Assert.NotNull(await repository.InsertAsync(ArtifactTestData.Write(record.Record.Id, account.Id), CancellationToken.None));
    }

    [Fact]
    public async Task List_ReturnsOnlyActiveArtifacts_NewestFirst_WithPaging()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var account = await RecordTestData.CreateAccountAsync(db);
        var record = await RecordTestData.Records(db).CreateAsync(account.Id, account.Id, RecordTestData.Write(), CancellationToken.None);
        var other = await RecordTestData.Records(db).CreateAsync(account.Id, account.Id, RecordTestData.Write(), CancellationToken.None);
        var a = await ArtifactTestData.AddAsync(db, record.Record.Id, account.Id, "a.pdf");
        var b = await ArtifactTestData.AddAsync(db, record.Record.Id, account.Id, "b.pdf");
        var c = await ArtifactTestData.AddAsync(db, record.Record.Id, account.Id, "c.pdf");
        await ArtifactTestData.AddAsync(db, other.Record.Id, account.Id, "elsewhere.pdf");
        await ArtifactTestData.Artifacts(db).RemoveAsync(b.Id, account.Id, CancellationToken.None);

        var firstPage = await ArtifactTestData.Artifacts(db).ListAsync(record.Record.Id, new RecordCaller(account.Id), false, 1, 1, CancellationToken.None);
        var secondPage = await ArtifactTestData.Artifacts(db).ListAsync(record.Record.Id, new RecordCaller(account.Id), false, 2, 1, CancellationToken.None);

        Assert.Equal(2, firstPage.TotalCount);
        Assert.Equal([c.Id], firstPage.Items.Select(x => x.Id));
        Assert.Equal([a.Id], secondPage.Items.Select(x => x.Id));
    }

    [Fact]
    public async Task List_CanBeFilteredToImages()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var account = await RecordTestData.CreateAccountAsync(db);
        var record = await RecordTestData.Records(db).CreateAsync(account.Id, account.Id, RecordTestData.Write(), CancellationToken.None);
        await ArtifactTestData.AddAsync(db, record.Record.Id, account.Id, "doc.pdf");
        var image = await ArtifactTestData.AddAsync(db, record.Record.Id, account.Id, "photo.png", isImage: true);

        var images = await ArtifactTestData.Artifacts(db).ListAsync(record.Record.Id, new RecordCaller(account.Id), true, 1, 20, CancellationToken.None);

        Assert.Equal(1, images.TotalCount);
        Assert.Equal(image.Id, Assert.Single(images.Items).Id);
    }

    [Fact]
    public async Task List_IsScopedToTheSubjectOrAnOfficer()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var owner = await RecordTestData.CreateAccountAsync(db);
        var stranger = await RecordTestData.CreateAccountAsync(db);
        var record = await RecordTestData.Records(db).CreateAsync(owner.Id, owner.Id, RecordTestData.Write(), CancellationToken.None);
        await ArtifactTestData.AddAsync(db, record.Record.Id, owner.Id);
        var repository = ArtifactTestData.Artifacts(db);

        Assert.Equal(0, (await repository.ListAsync(record.Record.Id, new RecordCaller(stranger.Id), false, 1, 20, CancellationToken.None)).TotalCount);
        Assert.Equal(1, (await repository.ListAsync(record.Record.Id, new RecordCaller(stranger.Id, IsOfficer: true), false, 1, 20, CancellationToken.None)).TotalCount);
    }

    [Fact]
    public async Task List_ReturnsNothingForALockedSealedRecord_UntilUnlocked()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var (subject, officer, record) = await ArtifactTestData.SealedServiceRecordAsync(db);
        await ArtifactTestData.AddAsync(db, record.Record.Id, officer.Id, "secret.pdf");
        var repository = ArtifactTestData.Artifacts(db);

        Assert.Equal(0, (await repository.ListAsync(record.Record.Id, new RecordCaller(subject.Id), false, 1, 20, CancellationToken.None)).TotalCount);
        Assert.Equal(1, (await repository.ListAsync(record.Record.Id, new RecordCaller(subject.Id, false, true), false, 1, 20, CancellationToken.None)).TotalCount);
        Assert.Equal(1, (await repository.ListAsync(record.Record.Id, new RecordCaller(officer.Id, true), false, 1, 20, CancellationToken.None)).TotalCount);
    }

    [Fact]
    public async Task Get_ReturnsTheArtifact_ForTheSubject_AndNullForOthers()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var owner = await RecordTestData.CreateAccountAsync(db);
        var stranger = await RecordTestData.CreateAccountAsync(db);
        var record = await RecordTestData.Records(db).CreateAsync(owner.Id, owner.Id, RecordTestData.Write(), CancellationToken.None);
        var artifact = await ArtifactTestData.AddAsync(db, record.Record.Id, owner.Id, "orders.pdf");
        var repository = ArtifactTestData.Artifacts(db);

        var found = await repository.GetAsync(artifact.Id, new RecordCaller(owner.Id), CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal("orders.pdf", found.FileName);
        Assert.False(found.IsLocked);
        Assert.Null(await repository.GetAsync(artifact.Id, new RecordCaller(stranger.Id), CancellationToken.None));
        Assert.Null(await repository.GetAsync(Guid.NewGuid(), new RecordCaller(owner.Id), CancellationToken.None));
    }

    [Fact]
    public async Task Get_RedactsALockedArtifact_AndRevealsItOnceUnlocked()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var (subject, officer, record) = await ArtifactTestData.SealedServiceRecordAsync(db);
        var artifact = await ArtifactTestData.AddAsync(db, record.Record.Id, officer.Id, "secret.pdf");
        var repository = ArtifactTestData.Artifacts(db);

        var locked = await repository.GetAsync(artifact.Id, new RecordCaller(subject.Id), CancellationToken.None);
        var unlocked = await repository.GetAsync(artifact.Id, new RecordCaller(subject.Id, false, true), CancellationToken.None);

        Assert.NotNull(locked);
        Assert.True(locked.IsLocked);
        Assert.Equal(record.Record.Id, locked.RecordId);
        Assert.Equal(string.Empty, locked.FileName);
        Assert.Equal(string.Empty, locked.BlobName);
        Assert.Null(locked.ThumbnailBlobName);
        Assert.NotNull(unlocked);
        Assert.False(unlocked.IsLocked);
        Assert.Equal("secret.pdf", unlocked.FileName);
    }

    [Fact]
    public async Task Remove_IsASoftDelete_ThatRecordsWhoRemovedIt()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var uploader = await RecordTestData.CreateAccountAsync(db);
        var remover = await RecordTestData.CreateAccountAsync(db);
        var record = await RecordTestData.Records(db).CreateAsync(uploader.Id, uploader.Id, RecordTestData.Write(), CancellationToken.None);
        var artifact = await ArtifactTestData.AddAsync(db, record.Record.Id, uploader.Id);
        var repository = ArtifactTestData.Artifacts(db);

        Assert.True(await repository.RemoveAsync(artifact.Id, remover.Id, CancellationToken.None));

        Assert.Equal(1L, await db.ScalarAsync<long>("SELECT count(*) FROM artifact"));
        Assert.NotNull(await db.ScalarAsync<string>("SELECT removed_at::text FROM artifact"));
        Assert.Equal(remover.Id, await db.ScalarAsync<Guid>("SELECT removed_by_account_id FROM artifact"));
        Assert.Null(await repository.GetAsync(artifact.Id, new RecordCaller(uploader.Id), CancellationToken.None));
        Assert.False(await repository.RemoveAsync(artifact.Id, remover.Id, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateDescription_ChangesTheText_OfActiveArtifactsOnly()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var account = await RecordTestData.CreateAccountAsync(db);
        var record = await RecordTestData.Records(db).CreateAsync(account.Id, account.Id, RecordTestData.Write(), CancellationToken.None);
        var image = await ArtifactTestData.AddAsync(db, record.Record.Id, account.Id, "photo.png", isImage: true);
        var removed = await ArtifactTestData.AddAsync(db, record.Record.Id, account.Id, "old.png", isImage: true);
        var repository = ArtifactTestData.Artifacts(db);
        await repository.RemoveAsync(removed.Id, account.Id, CancellationToken.None);

        var updated = await repository.UpdateDescriptionAsync(image.Id, "A temple at dawn", CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal("A temple at dawn", updated.Description);
        Assert.Equal(account.Id, updated.UploadedByAccountId);
        Assert.Null(await repository.UpdateDescriptionAsync(removed.Id, "Nope", CancellationToken.None));
        Assert.Null(await repository.UpdateDescriptionAsync(Guid.NewGuid(), "Nope", CancellationToken.None));
    }

    [Fact]
    public async Task ArtifactCounts_AreAvailableForRecords()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var account = await RecordTestData.CreateAccountAsync(db);
        var record = await RecordTestData.Records(db).CreateAsync(account.Id, account.Id, RecordTestData.Write(), CancellationToken.None);
        var artifact = await ArtifactTestData.AddAsync(db, record.Record.Id, account.Id);
        await ArtifactTestData.AddAsync(db, record.Record.Id, account.Id, "b.pdf");
        await ArtifactTestData.Artifacts(db).RemoveAsync(artifact.Id, account.Id, CancellationToken.None);

        var listed = await RecordTestData.Records(db).ListAsync(
            new RecordCaller(account.Id), RecordListScope.Mine, null, null, RecordSort.UpdatedDesc, 1, 20, CancellationToken.None);
        var loaded = await RecordTestData.Records(db).GetAsync(record.Record.Id, new RecordCaller(account.Id), CancellationToken.None);

        Assert.Equal(1, listed.Items.Single(r => r.Id == record.Record.Id).ArtifactCount);
        Assert.Equal(1, loaded!.Record.ArtifactCount);
    }
}
