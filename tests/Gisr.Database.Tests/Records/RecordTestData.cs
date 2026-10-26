using Gisr.Database.Accounts;
using Gisr.Database.Common;
using Gisr.Database.Records;
using Gisr.Database.Tests.Common;

namespace Gisr.Database.Tests.Records;

internal static class RecordTestData
{
    public static async Task<AccountRow> CreateAccountAsync(TestDatabase db, string name = "Test User") =>
        await new AccountRepository(db.DataSource, new SqlScriptLoader())
            .UpsertAsync(Guid.NewGuid(), Guid.NewGuid(), name, null, CancellationToken.None);

    public static RecordRepository Records(TestDatabase db) => new(db.DataSource, new SqlScriptLoader());

    public static RevisionRepository Revisions(TestDatabase db) => new(db.DataSource, new SqlScriptLoader());

    public static RecordWrite Write(
        string title = "Clone Wars service",
        string affiliation = "military",
        string detail = """{"summary":"Served","rank":"Captain"}""",
        IReadOnlyList<IncidentWrite>? incidents = null,
        IReadOnlyList<AwardWrite>? awards = null,
        DateOnly? start = null,
        DateOnly? end = null) =>
        new(title, affiliation, start, end, detail, incidents ?? [], awards ?? []);
}
