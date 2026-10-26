using System.Net;
using System.Text.Json;
using Bunit;
using Gisr.Shared.Accounts;
using Gisr.Web.Accounts;
using Gisr.Web.Tests.Common;

namespace Gisr.Web.Tests.Accounts;

public class ChainCodeFormTests : BunitContext
{
    private readonly FakeApi _api = new();
    private const string Path = "api/me/chain-code";

    public ChainCodeFormTests() => _api.AddTo(Services);

    [Fact]
    public void LoadFailure_IsNotTreatedAsAbsent_AndCanBeRetried()
    {
        _api.On(HttpMethod.Get, Path, _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var cut = Render<ChainCodeForm>();
        Assert.Empty(cut.FindAll("form"));
        Assert.Contains("could not be loaded", cut.Find("[role=alert]").TextContent, StringComparison.Ordinal);
        Assert.Equal("Retry", cut.Find(".card-footer button").TextContent.Trim());
        _api.On(HttpMethod.Get, Path, _ => new HttpResponseMessage(HttpStatusCode.NotFound));
        cut.Find("button").Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#chain-code-form")));
        Assert.Equal(12, cut.FindAll("input,select,textarea").Count);
        Assert.Equal(3, cut.FindAll("select").Count);
        Assert.Equal(4, cut.FindAll("textarea").Count);
        Assert.Empty(cut.FindAll("[required]"));
    }

    [Fact]
    public void ProfileFieldsets_GroupIdentityAndBiometrics_WithoutBirthDate()
    {
        var cut = Render<ChainCodeForm>();
        var groups = cut.FindAll("#chain-code-form .col-lg-6 > fieldset");
        Assert.Equal(2, groups.Count);
        Assert.Equal("Identity/Biographical Data", groups[0].QuerySelector("legend")!.TextContent.Trim());
        Assert.Equal(new[] { "chain-full-name", "chain-homeworld", "chain-species" }, groups[0].QuerySelectorAll("input,select").Select(field => field.Id));
        Assert.Equal("Biometric Data", groups[1].QuerySelector("legend")!.TextContent.Trim());
        Assert.Equal(new[] { "chain-height", "chain-eye-color", "chain-skin-color", "chain-hair-color" }, groups[1].QuerySelectorAll("input,select").Select(field => field.Id));
        Assert.All(groups, group =>
        {
            Assert.Contains("border", group.ClassList);
            Assert.Contains("rounded", group.ClassList);
            Assert.Contains("float-none", group.QuerySelector("legend")!.ClassList);
            Assert.Contains("w-auto", group.QuerySelector("legend")!.ClassList);
        });
        Assert.Equal(new[] { "chain-height", "chain-eye-color", "chain-skin-color", "chain-hair-color" }, groups[1].QuerySelectorAll(".row > .col-sm-6 > input,.row > .col-sm-6 > select").Select(field => field.Id));
        Assert.Empty(groups[0].QuerySelectorAll(".col-sm-6"));
        Assert.Empty(cut.FindAll("#chain-birth-date"));
        Assert.Equal(4, cut.FindAll("#chain-code-form .col-md-6 textarea").Count);
    }

    [Fact]
    public void SaveWithoutBirthDateControl_PreservesExistingBirthDate()
    {
        var profile = new UpdateChainCodeRequest { FullName = "Leia", BirthDate = "19 BBY" };
        var dto = new ChainCodeProfileDto(profile, "12345", DateTimeOffset.UtcNow);
        _api.OnJson(HttpMethod.Get, Path, dto);
        _api.OnJson(HttpMethod.Put, Path, dto);
        var cut = Render<ChainCodeForm>();
        Assert.Empty(cut.FindAll("#chain-birth-date"));
        cut.Find("form").Submit();

        using var body = JsonDocument.Parse(Assert.Single(_api.Calls, call => call.Method == HttpMethod.Put).Body!);
        Assert.Equal("19 BBY", body.RootElement.GetProperty("birthDate").GetString());
    }

    [Fact]
    public void GeneratedCode_IsCompactReadOnly_AndNotSubmittedAsProfileData()
    {
        var cut = Render<ChainCodeForm>();
        var code = cut.Find("input#chain-code-value[readonly]");
        Assert.Contains("p-1", code.ClassList);
        Assert.False(code.HasAttribute("name"));
        Assert.Equal("Your Chain Code", cut.Find("label[for=chain-code-value]").TextContent.Trim());
        Assert.NotNull(cut.Find(".card-body > div:first-child input#chain-code-value"));
        Assert.NotNull(cut.Find(".card-body > .row > .col-12.col-lg-6 input#chain-code-value"));
        Assert.Empty(cut.FindAll("label[for=chain-code-value] svg"));
        Assert.NotNull(cut.Find(".input-group.input-group-sm > .input-group-text:first-child svg[aria-hidden=true]"));
        Assert.NotNull(cut.Find(".input-group > .input-group-text + input#chain-code-value[readonly]"));
    }

    [Theory]
    [InlineData("eyeColors", "chain-eye-color", "eye color")]
    [InlineData("skinColors", "chain-skin-color", "skin color")]
    [InlineData("hairColors", "chain-hair-color", "hair color")]
    public void ColorTags_ReloadAddRemoveAndLinkCollectionErrors(string property, string id, string label)
    {
        var profile = JsonSerializer.Deserialize<UpdateChainCodeRequest>(JsonSerializer.Serialize(
            new Dictionary<string, string[]> { [property] = ["Blue", "Green"] }), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        _api.OnJson(HttpMethod.Get, Path, new ChainCodeProfileDto(profile, "12345", DateTimeOffset.UtcNow));
        _api.On(HttpMethod.Put, Path, _ => FakeApi.Problem(HttpStatusCode.BadRequest, new
        {
            status = 400, errors = new Dictionary<string, string[]> { [property] = ["Invalid color selection."] },
        }));
        var cut = Render<ChainCodeForm>();
        Assert.NotNull(cut.Find($"button[aria-label='Remove Green {label}']"));
        cut.Find($"button[aria-label='Remove Blue {label}']").Click();
        cut.Find($"#{id}").Change("Brown");
        cut.Find("form").Submit();

        using var body = JsonDocument.Parse(Assert.Single(_api.Calls, call => call.Method == HttpMethod.Put).Body!);
        Assert.Equal(new[] { "Brown", "Green" }, body.RootElement.GetProperty(property).EnumerateArray().Select(color => color.GetString()));
        Assert.Equal("true", cut.Find($"#{id}").GetAttribute("aria-invalid"));
        Assert.Equal($"{id}-error", cut.Find($"#{id}").GetAttribute("aria-describedby"));
        Assert.Contains("Invalid color", cut.Find($"#{id}-error").TextContent, StringComparison.Ordinal);
        Assert.NotNull(cut.Find($"button[aria-label='Remove Green {label}']"));
    }

    [Fact]
    public void SaveSuccess_UsesFooterAlertAfterButtons_AndAutoHides()
    {
        _api.OnJson(HttpMethod.Put, Path, new ChainCodeProfileDto(new UpdateChainCodeRequest { FullName = "Leia" }, "12345", DateTimeOffset.UtcNow));
        var cut = Render<ChainCodeForm>();
        cut.Find("#chain-full-name").Change("Leia");
        cut.Find("form").Submit();

        var status = cut.Find(".card-footer > [aria-live=polite] .alert-success[role=status]");
        Assert.Equal("Chain code updated...", status.TextContent.Trim());
        Assert.NotNull(status.QuerySelector("svg[aria-hidden=true]"));
        Assert.NotNull(cut.Find(".card-footer > .d-flex ~ [aria-live=polite] .alert-success"));
        Assert.Empty(cut.FindAll(".toast,.toast-container"));
        Assert.Empty(cut.FindAll(".card-body .alert-success"));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".alert-success")), TimeSpan.FromSeconds(7));
    }

    [Fact]
    public void SaveFailure_PreservesDraft_AndRetrySucceeds()
    {
        _api.On(HttpMethod.Put, Path, _ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var cut = Render<ChainCodeForm>();
        cut.Find("#chain-species").Change("Twi'lek");
        cut.Find("form").Submit();
        Assert.Equal("Twi'lek", cut.Find("#chain-species").GetAttribute("value"));
        Assert.Contains("could not be saved", cut.Find("[role=alert]").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll(".card-footer [role=status]"));
        _api.OnJson(HttpMethod.Put, Path, new ChainCodeProfileDto(new UpdateChainCodeRequest { Species = "Twi'lek" }, "12345", DateTimeOffset.UtcNow));
        cut.Find("form").Submit();
        cut.WaitForAssertion(() => Assert.Equal("12345", cut.Find("#chain-code-value").GetAttribute("value")));
        Assert.Empty(cut.FindAll("[role=alert]"));
    }

    [Fact]
    public void DeleteFailure_PreservesProfileAndConfirmation_AndCanBeRetried()
    {
        _api.OnJson(HttpMethod.Get, Path, new ChainCodeProfileDto(new UpdateChainCodeRequest { FullName = "Rex" }, "12345", DateTimeOffset.UtcNow));
        _api.On(HttpMethod.Delete, Path, _ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var cut = Render<ChainCodeForm>();
        cut.Find("#delete-chain-code").Click();
        Assert.NotNull(cut.Find(".card-footer #confirm-delete-chain-code"));
        Assert.NotNull(cut.Find(".card-footer #cancel-delete-chain-code"));
        cut.Find("#confirm-delete-chain-code").Click();
        Assert.Equal("Rex", cut.Find("#chain-full-name").GetAttribute("value"));
        Assert.Equal("12345", cut.Find("#chain-code-value").GetAttribute("value"));
        Assert.Contains("could not be deleted", cut.Find("[role=alert]").TextContent, StringComparison.Ordinal);
        _api.On(HttpMethod.Delete, Path, _ => new HttpResponseMessage(HttpStatusCode.NoContent));
        cut.Find("#confirm-delete-chain-code").Click();
        cut.WaitForAssertion(() => Assert.Equal("Not provided", cut.Find("#chain-code-value").GetAttribute("value")));
    }
}