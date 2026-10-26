using System.Reflection;
using System.Security.Claims;
using Gisr.Shared.Records;
using Gisr.Web.Records;

namespace Gisr.Web.Tests.Records;

public class ViewGrantStoreTests
{
    private static ClaimsPrincipal Principal(string claimType, string value) =>
        new(new ClaimsIdentity([new Claim(claimType, value)], "test"));

    [Fact]
    public void Register_StoresTheRecordAndToken_ForThatUserAndViewing()
    {
        var store = new ViewGrantStore();
        var recordId = Guid.NewGuid();

        var viewingId = store.Register("user-a", recordId, "token-1");

        Assert.NotEqual(Guid.Empty, viewingId);
        Assert.True(store.TryGet("user-a", viewingId, out var entry));
        Assert.Equal(recordId, entry.RecordId);
        Assert.Equal("token-1", entry.Token);
    }

    [Fact]
    public void TwoViewingsOfTheSameRecord_AreIndependent()
    {
        var store = new ViewGrantStore();
        var recordId = Guid.NewGuid();
        var first = store.Register("user-a", recordId, "token-1");
        var second = store.Register("user-a", recordId, "token-2");

        Assert.NotEqual(first, second);
        store.Remove("user-a", first);

        Assert.False(store.TryGet("user-a", first, out _));
        Assert.True(store.TryGet("user-a", second, out var entry));
        Assert.Equal("token-2", entry.Token);
    }

    [Fact]
    public void LookupWithAnotherUsersViewingId_Fails()
    {
        var store = new ViewGrantStore();
        var viewingId = store.Register("user-a", Guid.NewGuid(), "token-1");

        Assert.False(store.TryGet("user-b", viewingId, out var entry));
        Assert.Null(entry);
    }

    [Fact]
    public void Remove_OnlyAffectsTheCallersOwnEntry()
    {
        var store = new ViewGrantStore();
        var viewingId = store.Register("user-a", Guid.NewGuid(), "token-1");

        store.Remove("user-b", viewingId);

        Assert.True(store.TryGet("user-a", viewingId, out _));
    }

    [Fact]
    public void Remove_IsIdempotent_AndIgnoresUnknownIds()
    {
        var store = new ViewGrantStore();
        var viewingId = store.Register("user-a", Guid.NewGuid(), "token-1");

        store.Remove("user-a", viewingId);
        store.Remove("user-a", viewingId);
        store.Remove("user-a", Guid.NewGuid());

        Assert.False(store.TryGet("user-a", viewingId, out _));
    }

    [Fact]
    public void ViewingIds_AreRandomGuids()
    {
        var store = new ViewGrantStore();

        var ids = Enumerable.Range(0, 50).Select(_ => store.Register("user-a", Guid.NewGuid(), "t")).ToHashSet();

        Assert.Equal(50, ids.Count);
    }

    [Theory]
    [InlineData("oid", "11111111-1111-1111-1111-111111111111")]
    [InlineData("http://schemas.microsoft.com/identity/claims/objectidentifier", "22222222-2222-2222-2222-222222222222")]
    [InlineData("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier", "subject-3")]
    public void UserKey_ComesFromTheSignedInUsersIdentityClaims(string claimType, string value) =>
        Assert.Equal(value, ViewGrantStore.UserKey(Principal(claimType, value)));

    [Fact]
    public void UserKey_PrefersTheObjectId_AndFallsBackToAnAnonymousKey()
    {
        var both = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier", "sub"), new Claim("oid", "the-oid")], "test"));

        Assert.Equal("the-oid", ViewGrantStore.UserKey(both));
        Assert.Equal("anonymous", ViewGrantStore.UserKey(null));
        Assert.Equal("anonymous", ViewGrantStore.UserKey(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    [Fact]
    public void TokensAreNeverExposedThroughSharedDtos_ExceptTheUnlockResponseForTheWebServer()
    {
        var dtoTypes = typeof(RecordDto).Assembly.GetTypes().Where(t => t.Namespace?.StartsWith("Gisr.Shared", StringComparison.Ordinal) == true);

        var exposing = dtoTypes
            .Where(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Any(p => p.Name.Contains("Token", StringComparison.OrdinalIgnoreCase)))
            .Select(t => t.Name)
            .ToArray();

        Assert.Equal(["UnlockedRecordDto"], exposing);
    }

    [Fact]
    public void TheStore_OffersNoWayToEnumerateTokens()
    {
        var methods = typeof(ViewGrantStore).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Select(m => m.Name).Order();

        Assert.Equal(["Register", "Remove", "TryGet"], methods);
    }
}
