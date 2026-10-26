using System.Diagnostics.CodeAnalysis;
using System.Collections.Concurrent;
using System.Security.Claims;

namespace Gisr.Web.Records;

// Holds the view token of each override viewing on the Web server only; the browser never receives it.
// Entries are keyed by the signed-in user and a random per-viewing id, so two tabs never share or revoke each other's grant.
public sealed class ViewGrantStore
{
    public sealed record Entry(Guid RecordId, string Token);

    private readonly ConcurrentDictionary<(string User, Guid ViewingId), Entry> _entries = new();

    public static string UserKey(ClaimsPrincipal? user) =>
        user?.FindFirst("oid")?.Value
        ?? user?.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
        ?? user?.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? "anonymous";

    public Guid Register(string userKey, Guid recordId, string token)
    {
        var viewingId = Guid.NewGuid();
        _entries[(userKey, viewingId)] = new Entry(recordId, token);
        return viewingId;
    }

    public bool TryGet(string userKey, Guid viewingId, [NotNullWhen(true)] out Entry? entry) =>
        _entries.TryGetValue((userKey, viewingId), out entry);

    public void Remove(string userKey, Guid viewingId) => _entries.TryRemove((userKey, viewingId), out _);
}
