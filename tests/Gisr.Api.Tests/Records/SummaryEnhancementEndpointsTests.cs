using System.Net;
using System.Net.Http.Json;
using Gisr.Api.Tests.Common;
using Gisr.Shared.Records;
using Microsoft.Extensions.AI;

namespace Gisr.Api.Tests.Records;

[Collection("api")]
public class SummaryEnhancementEndpointsTests(PostgresFixture postgres)
{
    private const string Path = "/api/records/summary/enhance";

    [Fact]
    public async Task Anonymous_Returns401()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);

        var response = await factory.CreateClient().PostAsJsonAsync(Path, new EnhanceSummaryRequest("draft"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Draft_ReturnsTheModelText_AndSendsTheDraftAsDelimitedUserData()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        factory.Chat.Reply = _ => "  Served with distinction at Kamino.  ";

        var response = await factory.CreateUserClient().PostAsJsonAsync(Path, new EnhanceSummaryRequest("served at kamino"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Served with distinction at Kamino.", (await response.Content.ReadFromJsonAsync<EnhancedSummaryDto>())!.Summary);
        var request = Assert.Single(factory.Chat.Requests);
        Assert.Equal(ChatRole.System, request[0].Role);
        Assert.Equal(ChatRole.User, request[1].Role);
        Assert.Contains("<summary>\nserved at kamino\n</summary>", request[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnhancingDoesNotSaveAnything()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);

        await factory.CreateUserClient().PostAsJsonAsync(Path, new EnhanceSummaryRequest("draft"));

        Assert.Equal(0, await factory.ScalarAsync<long>("SELECT count(*) FROM record"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankDraft_Returns400_WithoutCallingTheModel(string draft)
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);

        var response = await factory.CreateUserClient().PostAsJsonAsync(Path, new EnhanceSummaryRequest(draft));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.Chat.Requests);
    }

    [Fact]
    public async Task DraftOverTheSummaryLimit_Returns400_WithoutCallingTheModel()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);

        var response = await factory.CreateUserClient().PostAsJsonAsync(Path, new EnhanceSummaryRequest(new string('a', EngagementSummary.MaxLength + 1)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.Chat.Requests);
    }

    [Fact]
    public async Task ModelFailure_Returns503_AndTheDraftIsNotLogged()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        factory.Chat.Failure = new InvalidOperationException("boom with secret-draft-text");

        var response = await factory.CreateUserClient().PostAsJsonAsync(Path, new EnhanceSummaryRequest("secret-draft-text"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("secret-draft-text", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain(factory.Logs.Messages, m => m.Contains("secret-draft-text", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyModelReply_Returns503(string reply)
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        factory.Chat.Reply = _ => reply;

        var response = await factory.CreateUserClient().PostAsJsonAsync(Path, new EnhanceSummaryRequest("draft"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task ModelReplyOverTheSummaryLimit_Returns503()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        factory.Chat.Reply = _ => new string('a', EngagementSummary.MaxLength + 1);

        var response = await factory.CreateUserClient().PostAsJsonAsync(Path, new EnhanceSummaryRequest("draft"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
