using Bunit;
using Gisr.Shared.Artifacts;
using Gisr.Shared.Common;
using Gisr.Web.Artifacts;
using Gisr.Web.Tests.Common;

namespace Gisr.Web.Tests.Artifacts;

public class NotSearchableBadgeTests : BunitContext
{
    private readonly FakeApi _api = new();
    private readonly Guid _recordId = Guid.NewGuid();

    public NotSearchableBadgeTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _api.AddTo(Services);
    }

    private IRenderedComponent<ArtifactList> RenderWith(string status, string? detail, bool isImage = false)
    {
        var artifact = new ArtifactDto(Guid.NewGuid(), _recordId, isImage ? "map.png" : "doc.pdf", isImage ? "image/png" : "application/pdf",
            100, isImage, isImage ? "A map" : null, "Rex", DateTimeOffset.UtcNow, status, detail);
        _api.OnJson(HttpMethod.Get, $"api/records/{_recordId}/artifacts?pageSize=100", new PagedResult<ArtifactDto>(1, 100, 1, [artifact]));
        return Render<ArtifactList>(p => p.Add(c => c.RecordId, _recordId));
    }

    [Theory]
    [InlineData("unsupported_type", "This file type is not searched.", false)]
    [InlineData("unsupported_type", "Images are not searched by content.", true)]
    [InlineData("no_text", "No readable text was found.", false)]
    [InlineData("ocr_disabled", "Text recognition for scanned files is turned off.", false)]
    [InlineData("ocr_budget_exhausted", "The monthly text-recognition budget is used up; it is retried next month.", false)]
    [InlineData("parse_error", "The file could not be read.", false)]
    [InlineData("too_large", "The file is too large to index.", false)]
    public void AFileThatWasSkipped_IsNotSearchable_WithTheReasonForEachCode(string detail, string reason, bool isImage)
    {
        var cut = RenderWith("skipped", detail, isImage);

        Assert.Equal("Not searchable", cut.Find(".artifact-search-badge").TextContent.Trim());
        Assert.Equal(reason, cut.Find(".artifact-search-reason").TextContent.Trim());
        Assert.Equal(reason, cut.Find(".artifact-search-badge").GetAttribute("title"));
        Assert.Contains("visually-hidden", cut.Find(".artifact-search-reason").ClassList);
    }

    [Fact]
    public void AFailedFile_IsNotSearchable_WithItsParseError()
    {
        var cut = RenderWith("failed", "parse_error");

        Assert.Equal("Not searchable", cut.Find(".artifact-search-badge").TextContent.Trim());
        Assert.Equal("The file could not be read.", cut.Find(".artifact-search-reason").TextContent.Trim());
    }

    [Fact]
    public void AFailedFile_WithoutADetail_StillExplainsItself()
    {
        var cut = RenderWith("failed", null);

        Assert.Equal("Not searchable", cut.Find(".artifact-search-badge").TextContent.Trim());
        Assert.Equal("Indexing failed.", cut.Find(".artifact-search-reason").TextContent.Trim());
    }

    [Fact]
    public void APartlyReadScan_IsSearchable_ButNotesThePageLimit()
    {
        var cut = RenderWith("indexed", "ocr_page_limit");

        Assert.Empty(cut.FindAll(".artifact-search-badge"));
        Assert.Equal("Only the first 100 pages were read.", cut.Find(".artifact-search-reason").TextContent.Trim());
    }

    [Fact]
    public void AFileWaitingToBeIndexed_SaysSo_WithoutCallingItNotSearchable()
    {
        var cut = RenderWith("pending", null);

        Assert.Equal("Indexing pending", cut.Find(".artifact-search-badge").TextContent.Trim());
        Assert.DoesNotContain("Not searchable", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll(".artifact-search-reason"));
    }

    [Fact]
    public void AnIndexedFile_CarriesNoBadgeAtAll()
    {
        var cut = RenderWith("indexed", null);

        Assert.Empty(cut.FindAll(".artifact-search-badge"));
        Assert.Empty(cut.FindAll(".artifact-search-reason"));
    }
}
