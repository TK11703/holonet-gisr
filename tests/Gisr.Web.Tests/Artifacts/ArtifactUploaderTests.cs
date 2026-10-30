using System.Net;
using Bunit;
using Gisr.Shared.Artifacts;
using Gisr.Web.Artifacts;
using Gisr.Web.Tests.Common;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Gisr.Web.Tests.Artifacts;

public class ArtifactUploaderTests : BunitContext
{
    private readonly FakeApi _api = new();
    private readonly Guid _recordId = Guid.NewGuid();
    private readonly List<ArtifactDto> _uploaded = [];

    public ArtifactUploaderTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _api.AddTo(Services);
    }

    private string UploadPath => $"api/records/{_recordId}/artifacts";

    private IRenderedComponent<ArtifactUploader> RenderUploader() =>
        Render<ArtifactUploader>(p =>
        {
            p.Add(c => c.RecordId, _recordId);
            p.Add(c => c.OnUploaded, EventCallback.Factory.Create<ArtifactDto>(this, dto => _uploaded.Add(dto)));
        });

    private static ArtifactDto Dto(string name, bool isImage = false, string? description = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), name, isImage ? "image/png" : "application/pdf", 10, isImage, description, "Rex",
            DateTimeOffset.UtcNow, isImage ? "skipped" : "pending", isImage ? "unsupported_type" : null);

    private static void Pick(IRenderedComponent<ArtifactUploader> cut, params (string Name, int Size)[] files) =>
        cut.FindComponent<InputFile>().UploadFiles([.. files.Select(f => InputFileContent.CreateFromBinary(new byte[f.Size], f.Name))]);

    [Fact]
    public void TheFilePicker_AcceptsMultipleFiles_OfTheAllowedKindsOnly()
    {
        var cut = RenderUploader();

        var input = cut.Find("input#artifact-file");
        Assert.Equal("file", input.GetAttribute("type"));
        Assert.True(input.HasAttribute("multiple"));
        var accept = input.GetAttribute("accept")!;
        foreach (var extension in new[] { ".jpg", ".png", ".webp", ".gif", ".pdf", ".docx", ".txt", ".md" })
        {
            Assert.Contains(extension, accept, StringComparison.Ordinal);
        }

        Assert.Equal("Choose files", cut.Find("label[for=artifact-file]").TextContent.Trim().Split(" or ")[0]);
        Assert.Contains("25 MB", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMaximumFileSizeIs25MB() => Assert.Equal(26_214_400, ArtifactUploader.MaxFileSize);

    [Fact]
    public void NothingIsUploadable_UntilFilesAreChosen()
    {
        var cut = RenderUploader();

        Assert.True(cut.Find("#upload-artifacts").HasAttribute("disabled"));
        Assert.Empty(cut.FindAll("li.artifact-item"));
    }

    [Fact]
    public void ChosenFiles_AreListedWithTheirSize_BeforeAnythingIsSent()
    {
        var cut = RenderUploader();

        Pick(cut, ("orders.pdf", 2048));

        var item = Assert.Single(cut.FindAll("li.artifact-item"));
        Assert.Contains("orders.pdf", item.TextContent, StringComparison.Ordinal);
        Assert.Contains("2.0 KB", item.TextContent, StringComparison.Ordinal);
        Assert.False(cut.Find("#upload-artifacts").HasAttribute("disabled"));
        Assert.Empty(_api.Calls);
    }

    [Fact]
    public void AFileOver25MB_IsRefusedLocally_AndNeverSent()
    {
        var cut = RenderUploader();

        Pick(cut, ("huge.pdf", 26_214_401));

        var item = Assert.Single(cut.FindAll("li.artifact-item"));
        Assert.Contains("larger than 25 MB", item.QuerySelector(".artifact-error")!.TextContent, StringComparison.Ordinal);
        Assert.True(cut.Find("#upload-artifacts").HasAttribute("disabled"));
        Assert.Empty(_api.Calls);
    }

    [Fact]
    public void UploadingAFile_PostsItToTheRecord_AndReportsTheResult()
    {
        var created = Dto("orders.pdf");
        _api.OnJson(HttpMethod.Post, UploadPath, created, HttpStatusCode.Created);
        var cut = RenderUploader();
        Pick(cut, ("orders.pdf", 2048));

        cut.Find("#upload-artifacts").Click();

        cut.WaitForAssertion(() => Assert.Contains("Uploaded", cut.Find("li.artifact-item .artifact-status").TextContent, StringComparison.Ordinal));
        var call = Assert.Single(_api.Calls);
        Assert.Equal(HttpMethod.Post, call.Method);
        Assert.Contains("orders.pdf", call.Body, StringComparison.Ordinal);
        Assert.Equal(created.Id, Assert.Single(_uploaded).Id);
    }

    [Fact]
    public void AfterUploading_ASuccessMessageShows_ThenTheUploadedItemsAreRemoved()
    {
        _api.OnJson(HttpMethod.Post, UploadPath, Dto("orders.pdf"), HttpStatusCode.Created);
        var cut = Render<ArtifactUploader>(p =>
        {
            p.Add(c => c.RecordId, _recordId);
            p.Add(c => c.SuccessDuration, TimeSpan.FromMilliseconds(200));
        });
        Pick(cut, ("orders.pdf", 2048));

        cut.Find("#upload-artifacts").Click();

        cut.WaitForAssertion(() => Assert.Contains("uploaded successfully", cut.Find("#artifact-upload-success").TextContent, StringComparison.Ordinal));
        Assert.Single(cut.FindAll("li.artifact-item"));
        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("#artifact-upload-success"));
            Assert.Empty(cut.FindAll("li.artifact-item"));
        }, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void AnImage_NeedsADescription_BeforeItCanBeUploaded()
    {
        var cut = RenderUploader();
        Pick(cut, ("map.png", 100));

        var description = cut.Find("li.artifact-item input.artifact-description");
        Assert.Contains("map.png", description.GetAttribute("aria-label"), StringComparison.Ordinal);
        Assert.Equal("true", description.GetAttribute("aria-required"));
        cut.Find("#upload-artifacts").Click();

        cut.WaitForAssertion(() => Assert.Contains("Describe this image", cut.Find("li.artifact-item .artifact-error").TextContent, StringComparison.Ordinal));
        Assert.Empty(_api.Calls);
        Assert.Equal("true", cut.Find("li.artifact-item input.artifact-description").GetAttribute("aria-invalid"));
    }

    [Fact]
    public void TheDescriptionError_Clears_WhenADescriptionIsTyped()
    {
        var cut = RenderUploader();
        Pick(cut, ("map.png", 100));
        cut.Find("#upload-artifacts").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("li.artifact-item .artifact-error")));

        cut.Find("li.artifact-item input.artifact-description").Input("A star map");

        Assert.Empty(cut.FindAll("li.artifact-item .artifact-error"));
        Assert.False(cut.Find("#upload-artifacts").HasAttribute("disabled"));
    }

    [Fact]
    public void AnImageWithADescription_IsUploadedWithIt()
    {
        _api.OnJson(HttpMethod.Post, UploadPath, Dto("map.png", true, "A star map"), HttpStatusCode.Created);
        var cut = RenderUploader();
        Pick(cut, ("map.png", 100));
        cut.Find("li.artifact-item input.artifact-description").Input("A star map");

        cut.Find("#upload-artifacts").Click();

        cut.WaitForAssertion(() => Assert.Single(_uploaded));
        var body = Assert.Single(_api.Calls).Body!;
        Assert.Contains("name=description", body, StringComparison.Ordinal);
        Assert.Contains("A star map", body, StringComparison.Ordinal);
    }

    [Fact]
    public void ANonImage_NeedsNoDescriptionField()
    {
        var cut = RenderUploader();

        Pick(cut, ("orders.pdf", 100));

        Assert.Empty(cut.FindAll("input.artifact-description"));
    }

    [Fact]
    public void ARejectedFile_ShowsTheServersReason_AndTheOthersStillUpload()
    {
        var calls = 0;
        _api.On(HttpMethod.Post, UploadPath, _ => Interlocked.Increment(ref calls) == 1
            ? FakeApi.Problem(HttpStatusCode.UnsupportedMediaType, new { title = "Unsupported Media Type", status = 415, detail = "This file type is not allowed." })
            : FakeApi.JsonResponse(HttpStatusCode.Created, Dto("good.pdf")));
        var cut = RenderUploader();
        Pick(cut, ("evil.exe", 100), ("good.pdf", 100));

        cut.Find("#upload-artifacts").Click();

        cut.WaitForAssertion(() =>
        {
            var items = cut.FindAll("li.artifact-item");
            Assert.Contains("This file type is not allowed", items[0].QuerySelector(".artifact-error")!.TextContent, StringComparison.Ordinal);
            Assert.Contains("Uploaded", items[1].QuerySelector(".artifact-status")!.TextContent, StringComparison.Ordinal);
        });
        Assert.Single(_uploaded);
    }

    [Fact]
    public void AValidationFailure_ShowsTheFirstMessage()
    {
        _api.On(HttpMethod.Post, UploadPath, _ => FakeApi.Problem(HttpStatusCode.BadRequest, new
        {
            title = "One or more validation errors occurred.",
            status = 400,
            errors = new Dictionary<string, string[]> { ["file"] = ["Max 50 active artifacts per record."] },
        }));
        var cut = RenderUploader();
        Pick(cut, ("orders.pdf", 100));

        cut.Find("#upload-artifacts").Click();

        cut.WaitForAssertion(() =>
            Assert.Contains("Max 50 active artifacts per record.", cut.Find("li.artifact-item .artifact-error").TextContent, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, "larger than 25 MB")]
    [InlineData(HttpStatusCode.Forbidden, "not allowed to add files")]
    [InlineData(HttpStatusCode.BadGateway, "Upload failed")]
    public void OtherFailures_GetAClearMessage(HttpStatusCode status, string expected)
    {
        _api.On(HttpMethod.Post, UploadPath, _ => FakeApi.Problem(status, new { title = "Error", status = (int)status }));
        var cut = RenderUploader();
        Pick(cut, ("orders.pdf", 100));

        cut.Find("#upload-artifacts").Click();

        cut.WaitForAssertion(() =>
            Assert.Contains(expected, cut.Find("li.artifact-item .artifact-error").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void AFailedFile_CanBeRetried()
    {
        var calls = 0;
        _api.On(HttpMethod.Post, UploadPath, _ => Interlocked.Increment(ref calls) == 1
            ? FakeApi.Problem(HttpStatusCode.BadGateway, new { status = 502 })
            : FakeApi.JsonResponse(HttpStatusCode.Created, Dto("orders.pdf")));
        var cut = RenderUploader();
        Pick(cut, ("orders.pdf", 100));
        cut.Find("#upload-artifacts").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".artifact-error")));

        cut.Find("#upload-artifacts").Click();

        cut.WaitForAssertion(() => Assert.Contains("Uploaded", cut.Find(".artifact-status").TextContent, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll(".artifact-error"));
        Assert.Single(_uploaded);
    }

    [Fact]
    public void AnUploadedFile_IsNotSentAgain()
    {
        _api.OnJson(HttpMethod.Post, UploadPath, Dto("orders.pdf"), HttpStatusCode.Created);
        var cut = RenderUploader();
        Pick(cut, ("orders.pdf", 100));
        cut.Find("#upload-artifacts").Click();
        cut.WaitForAssertion(() => Assert.Single(_uploaded));

        Assert.True(cut.Find("#upload-artifacts").HasAttribute("disabled"));
        Assert.Single(_api.Calls);
    }

    [Fact]
    public void AFileCanBeDroppedFromTheQueue_BeforeUploading()
    {
        var cut = RenderUploader();
        Pick(cut, ("a.pdf", 10), ("b.pdf", 10));

        cut.Find("li.artifact-item button.artifact-dismiss").Click();

        var item = Assert.Single(cut.FindAll("li.artifact-item"));
        Assert.Contains("b.pdf", item.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDismissButton_HasAnAccessibleName()
    {
        var cut = RenderUploader();
        Pick(cut, ("a.pdf", 10));

        Assert.Equal("Remove a.pdf from the list", cut.Find("button.artifact-dismiss").GetAttribute("aria-label"));
    }
}
