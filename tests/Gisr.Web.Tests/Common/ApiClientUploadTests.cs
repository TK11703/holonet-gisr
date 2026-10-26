using System.Net;
using Gisr.Web.Common;
using Gisr.Web.Tests.Common;

namespace Gisr.Web.Tests.Common;

public class ApiClientUploadTests
{
    private sealed record Created(Guid Id);

    [Fact]
    public async Task UploadAsync_PostsAMultipartForm_WithTheFileAndFields_AndABearerToken()
    {
        var api = new FakeApi();
        var id = Guid.NewGuid();
        api.OnJson(HttpMethod.Post, "api/records/abc/artifacts", new Created(id), HttpStatusCode.Created);
        var client = api.CreateClient();
        await using var content = new MemoryStream("hello world"u8.ToArray());

        var created = await client.UploadAsync<Created>(
            "api/records/abc/artifacts", "notes.txt", content, new Dictionary<string, string> { ["description"] = "My notes" });

        Assert.Equal(id, created!.Id);
        var call = Assert.Single(api.Calls);
        Assert.Equal(HttpMethod.Post, call.Method);
        Assert.Contains("name=file", call.Body, StringComparison.Ordinal);
        Assert.Contains("notes.txt", call.Body, StringComparison.Ordinal);
        Assert.Contains("hello world", call.Body, StringComparison.Ordinal);
        Assert.Contains("name=description", call.Body, StringComparison.Ordinal);
        Assert.Contains("My notes", call.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UploadAsync_Throws_AnApiException_ForProblemResponses()
    {
        var api = new FakeApi();
        api.On(HttpMethod.Post, "api/records/abc/artifacts", _ => FakeApi.Problem(HttpStatusCode.UnsupportedMediaType, new { title = "Unsupported Media Type", status = 415, detail = "Not allowed." }));
        var client = api.CreateClient();
        await using var content = new MemoryStream([1, 2, 3]);

        var ex = await Assert.ThrowsAsync<ApiException>(() => client.UploadAsync<object>("api/records/abc/artifacts", "x.exe", content));

        Assert.Equal(415, ex.Status);
        Assert.Equal("Not allowed.", ex.Detail);
    }
}
