using Gisr.Database.Gallery;
using Gisr.Database.Common;
using Gisr.Database.Records;
using Gisr.Database.Tests.Artifacts;
using Gisr.Database.Tests.Common;
using Gisr.Database.Tests.Records;

namespace Gisr.Database.Tests.Gallery;

[Collection("postgres")]
public class GalleryRepositoryTests(PostgresFixture postgres)
{
    private static GalleryRepository Gallery(TestDatabase db) => new(db.DataSource, new SqlScriptLoader());

    private static Task<PagedRows<GalleryGroupRow>> ListAsync(TestDatabase db, RecordCaller caller, RecordListScope scope = RecordListScope.Mine, int page = 1, int pageSize = 12, GallerySort sort = GallerySort.UpdatedDesc) =>
        Gallery(db).ListGroupsAsync(caller, scope, sort, page, pageSize, CancellationToken.None);

    private static async Task<Guid> RecordWithImagesAsync(TestDatabase db, Gisr.Database.Accounts.AccountRow subject, int images, string title = "Clone Wars service")
    {
        var record = await RecordTestData.Records(db).CreateAsync(subject.Id, subject.Id, RecordTestData.Write(title: title), CancellationToken.None);
        for (var i = 0; i < images; i++)
        {
            await ArtifactTestData.AddAsync(db, record.Record.Id, subject.Id, $"photo{i}.png", isImage: true, description: $"Photo {i}");
        }

        return record.Record.Id;
    }

    [Fact]
    public async Task ReturnsOneGroupPerRecordWithImages_WithTheCountAndUpToFourNewestCovers()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var account = await RecordTestData.CreateAccountAsync(db, "Rex");
        var big = await RecordWithImagesAsync(db, account, 6, "Big album");
        var small = await RecordWithImagesAsync(db, account, 1, "Small album");
        await RecordWithImagesAsync(db, account, 0, "No images");
        await ArtifactTestData.AddAsync(db, big, account.Id, "orders.pdf");
        var newest = await db.ScalarAsync<Guid>($"SELECT id FROM artifact WHERE record_id = '{big}' AND is_image ORDER BY uploaded_at DESC, id DESC LIMIT 1");

        var result = await ListAsync(db, new RecordCaller(account.Id));

        Assert.Equal(2, result.TotalCount);
        var group = result.Items.Single(g => g.RecordId == big);
        Assert.Equal("Big album", group.Title);
        Assert.Equal("military", group.Affiliation);
        Assert.Equal("Rex", group.SubjectDisplayName);
        Assert.Equal(6, group.ImageCount);
        Assert.Equal(4, group.CoverArtifactIds.Length);
        Assert.Equal(newest, group.CoverArtifactIds[0]);
        Assert.Equal(1, result.Items.Single(g => g.RecordId == small).ImageCount);
        Assert.DoesNotContain(result.Items, g => g.Title == "No images");
    }

    [Fact]
    public async Task RemovedImages_AreNotCountedOrUsedAsCovers_AndAnAllRemovedRecordDisappears()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var account = await RecordTestData.CreateAccountAsync(db);
        var partly = await RecordWithImagesAsync(db, account, 3, "Partly removed");
        var gone = await RecordWithImagesAsync(db, account, 1, "All removed");
        var removedCover = await db.ScalarAsync<Guid>($"SELECT id FROM artifact WHERE record_id = '{partly}' ORDER BY uploaded_at DESC, id DESC LIMIT 1");
        await ArtifactTestData.Artifacts(db).RemoveAsync(removedCover, account.Id, CancellationToken.None);
        await db.ExecuteAsync($"UPDATE artifact SET removed_at = now(), removed_by_account_id = '{account.Id}' WHERE record_id = '{gone}'");

        var result = await ListAsync(db, new RecordCaller(account.Id));

        var group = Assert.Single(result.Items);
        Assert.Equal(partly, group.RecordId);
        Assert.Equal(2, group.ImageCount);
        Assert.DoesNotContain(removedCover, group.CoverArtifactIds);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task SealedRecords_NeverAppear_ForAnyCaller_IncludingOfficersAndTheSubject()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var (subject, officer, sealedRecord) = await ArtifactTestData.SealedServiceRecordAsync(db);
        await ArtifactTestData.AddAsync(db, sealedRecord.Record.Id, officer.Id, "secret.png", isImage: true, description: "Secret");
        var open = await RecordWithImagesAsync(db, subject, 1, "Open album");

        var subjectMine = await ListAsync(db, new RecordCaller(subject.Id));
        var officerAll = await ListAsync(db, new RecordCaller(officer.Id, true), RecordListScope.All);
        var subjectUnlocked = await ListAsync(db, new RecordCaller(subject.Id, false, true));

        Assert.Equal([open], subjectMine.Items.Select(g => g.RecordId));
        Assert.Equal([open], officerAll.Items.Select(g => g.RecordId));
        Assert.Equal([open], subjectUnlocked.Items.Select(g => g.RecordId));
        Assert.Equal(1, officerAll.TotalCount);
    }

    [Fact]
    public async Task MineShowsOnlyTheCallersOwnRecords_AndAllNeedsAnOfficer()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var luke = await RecordTestData.CreateAccountAsync(db, "Luke");
        var leia = await RecordTestData.CreateAccountAsync(db, "Leia");
        var officer = await RecordTestData.CreateAccountAsync(db, "Officer");
        var lukes = await RecordWithImagesAsync(db, luke, 1, "Luke album");
        var leias = await RecordWithImagesAsync(db, leia, 1, "Leia album");
        var officers = await RecordWithImagesAsync(db, officer, 1, "Officer album");

        var lukeMine = await ListAsync(db, new RecordCaller(luke.Id));
        var lukeAll = await ListAsync(db, new RecordCaller(luke.Id), RecordListScope.All);
        var officerMine = await ListAsync(db, new RecordCaller(officer.Id, true));
        var officerAll = await ListAsync(db, new RecordCaller(officer.Id, true), RecordListScope.All);

        Assert.Equal([lukes], lukeMine.Items.Select(g => g.RecordId));
        Assert.Empty(lukeAll.Items);
        Assert.Equal(0, lukeAll.TotalCount);
        Assert.Equal([officers], officerMine.Items.Select(g => g.RecordId));
        Assert.Equal(3, officerAll.TotalCount);
        Assert.Contains(lukes, officerAll.Items.Select(g => g.RecordId));
        Assert.Contains(leias, officerAll.Items.Select(g => g.RecordId));
    }

    [Fact]
    public async Task Groups_AreOrderedByTheirNewestImage_AndPaged()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var account = await RecordTestData.CreateAccountAsync(db);
        var first = await RecordWithImagesAsync(db, account, 1, "First");
        var second = await RecordWithImagesAsync(db, account, 1, "Second");
        var third = await RecordWithImagesAsync(db, account, 1, "Third");

        var page1 = await ListAsync(db, new RecordCaller(account.Id), pageSize: 2);
        var page2 = await ListAsync(db, new RecordCaller(account.Id), page: 2, pageSize: 2);

        Assert.Equal(3, page1.TotalCount);
        Assert.Equal([third, second], page1.Items.Select(g => g.RecordId));
        Assert.Equal([first], page2.Items.Select(g => g.RecordId));
    }

    [Fact]
    public async Task TitleSorts_OrderGroupsAlphabetically_AndPage()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var account = await RecordTestData.CreateAccountAsync(db);
        var bravo = await RecordWithImagesAsync(db, account, 1, "Bravo");
        var charlie = await RecordWithImagesAsync(db, account, 1, "Charlie");
        var alpha = await RecordWithImagesAsync(db, account, 1, "Alpha");
        var caller = new RecordCaller(account.Id);

        var ascending = await ListAsync(db, caller, sort: GallerySort.TitleAsc);
        var descending = await ListAsync(db, caller, sort: GallerySort.TitleDesc);
        var ascendingPage2 = await ListAsync(db, caller, page: 2, pageSize: 2, sort: GallerySort.TitleAsc);

        Assert.Equal([alpha, bravo, charlie], ascending.Items.Select(g => g.RecordId));
        Assert.Equal([charlie, bravo, alpha], descending.Items.Select(g => g.RecordId));
        Assert.Equal([charlie], ascendingPage2.Items.Select(g => g.RecordId));
    }

    [Fact]
    public async Task AnImageAddedLater_MovesItsRecordToTheFront()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var account = await RecordTestData.CreateAccountAsync(db);
        var first = await RecordWithImagesAsync(db, account, 1, "First");
        await RecordWithImagesAsync(db, account, 1, "Second");
        var record = await RecordTestData.Records(db).GetAsync(first, new RecordCaller(account.Id), CancellationToken.None);
        await ArtifactTestData.AddAsync(db, record!.Record.Id, account.Id, "later.png", isImage: true, description: "Later");

        var result = await ListAsync(db, new RecordCaller(account.Id));

        Assert.Equal(first, result.Items[0].RecordId);
        Assert.Equal(2, result.Items[0].ImageCount);
    }
}
