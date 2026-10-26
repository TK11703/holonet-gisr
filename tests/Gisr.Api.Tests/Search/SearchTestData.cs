using System.Net;
using System.Net.Http.Json;
using Gisr.Api.Search;
using Gisr.Api.Tests.Common;
using Gisr.Shared.Records;
using Microsoft.Extensions.DependencyInjection;

namespace Gisr.Api.Tests.Search;

internal static class SearchTestData
{
    public static CreateRecordRequest Request(string title, string summary, Guid? subjectOid = null, bool sealedRecord = false, string affiliation = "military") =>
        new(title, affiliation, new DateOnly(2019, 1, 1), new DateOnly(2020, 1, 1),
            affiliation == "military"
                ? new Dictionary<string, string> { ["summary"] = summary, ["branch"] = "Navy", ["rank"] = "Admiral" }
                : new Dictionary<string, string> { ["summary"] = summary, ["order_rank"] = "Knight" },
            [], [], subjectOid, sealedRecord, sealedRecord ? "open sesame 42" : null);

    public static async Task<RecordDto> CreateRecordAsync(HttpClient client, string title, string summary, Guid? subjectOid = null, bool sealedRecord = false, string affiliation = "military")
    {
        var response = await client.PostAsJsonAsync("/api/records", Request(title, summary, subjectOid, sealedRecord, affiliation));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RecordDto>())!;
    }

    public static async Task<int> IndexAllAsync(ApiFactory factory)
    {
        var worker = factory.Services.GetRequiredService<SearchIndexingWorker>();
        var processed = 0;
        while (processed < 500 && await worker.ProcessNextAsync(CancellationToken.None))
        {
            processed++;
        }

        return processed;
    }
}
