using Gisr.Api.Artifacts;

namespace Gisr.Api.Tests.Artifacts;

public class ArtifactValidatorTests
{
    private readonly ArtifactUploadValidator _validator = new();

    private static ArtifactUpload Upload(
        string fileName = "orders.pdf",
        long size = 1024,
        bool isImage = false,
        string? description = null,
        int activeCount = 0) => new(fileName, size, isImage, description, activeCount);

    private IEnumerable<string> Errors(ArtifactUpload upload) =>
        _validator.Validate(upload).Errors.Select(e => e.PropertyName);

    [Fact]
    public void AValidUpload_PassesWithoutADescription_WhenItIsNotAnImage() =>
        Assert.True(_validator.Validate(Upload()).IsValid);

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(ArtifactLimits.MaxBytes + 1)]
    public void SizeMustBeBetween1AndTheCap(long size) =>
        Assert.Contains(nameof(ArtifactUpload.SizeBytes), Errors(Upload(size: size)));

    [Theory]
    [InlineData(1)]
    [InlineData(ArtifactLimits.MaxBytes)]
    public void SizeAtTheBoundsIsAccepted(long size) =>
        Assert.True(_validator.Validate(Upload(size: size)).IsValid);

    [Fact]
    public void TheFileNameIsRequired_AndCappedAt255Characters()
    {
        Assert.Contains(nameof(ArtifactUpload.FileName), Errors(Upload(fileName: "")));
        Assert.Contains(nameof(ArtifactUpload.FileName), Errors(Upload(fileName: new string('a', 256))));
        Assert.True(_validator.Validate(Upload(fileName: new string('a', 255))).IsValid);
    }

    [Fact]
    public void ImagesRequireADescription()
    {
        Assert.Contains(nameof(ArtifactUpload.Description), Errors(Upload(isImage: true)));
        Assert.Contains(nameof(ArtifactUpload.Description), Errors(Upload(isImage: true, description: "   ")));
        Assert.True(_validator.Validate(Upload(isImage: true, description: "A Jedi temple")).IsValid);
    }

    [Fact]
    public void ADescriptionIsCappedAt500Characters()
    {
        Assert.True(_validator.Validate(Upload(isImage: true, description: new string('d', 500))).IsValid);
        Assert.Contains(nameof(ArtifactUpload.Description), Errors(Upload(isImage: true, description: new string('d', 501))));
        Assert.Contains(nameof(ArtifactUpload.Description), Errors(Upload(description: new string('d', 501))));
    }

    [Fact]
    public void ARecordAcceptsAtMost50ActiveArtifacts()
    {
        Assert.True(_validator.Validate(Upload(activeCount: 49)).IsValid);
        var result = _validator.Validate(Upload(activeCount: 50));

        Assert.Equal(nameof(ArtifactUpload.ActiveCount), Assert.Single(result.Errors).PropertyName);
        Assert.Equal("Max 50 active artifacts per record.", result.Errors[0].ErrorMessage);
    }

    [Fact]
    public void UpdateRequest_RequiresANonBlankDescriptionOfAtMost500Characters()
    {
        var validator = new UpdateArtifactRequestValidator();

        Assert.False(validator.Validate(new Gisr.Shared.Artifacts.UpdateArtifactRequest("")).IsValid);
        Assert.False(validator.Validate(new Gisr.Shared.Artifacts.UpdateArtifactRequest("   ")).IsValid);
        Assert.False(validator.Validate(new Gisr.Shared.Artifacts.UpdateArtifactRequest(new string('d', 501))).IsValid);
        Assert.True(validator.Validate(new Gisr.Shared.Artifacts.UpdateArtifactRequest(new string('d', 500))).IsValid);
    }
}

public class ArtifactFileNameTests
{
    [Theory]
    [InlineData("orders.pdf", "orders.pdf")]
    [InlineData("../../etc/passwd.txt", "passwd.txt")]
    [InlineData(@"C:\Users\me\secret.docx", "secret.docx")]
    [InlineData("  spaced name .md ", "spaced name .md")]
    [InlineData("bad\u0000na\tme\u001f.txt", "badname.txt")]
    [InlineData("quote\"<>|?*name.png", "quotename.png")]
    public void Sanitize_StripsPathsAndUnsafeCharacters(string input, string expected) =>
        Assert.Equal(expected, ArtifactFileName.Sanitize(input));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("../")]
    [InlineData("\u0000")]
    public void Sanitize_ReturnsAnEmptyNameWhenNothingSurvives(string input) =>
        Assert.Equal(string.Empty, ArtifactFileName.Sanitize(input));

    [Fact]
    public void Sanitize_TruncatesLongNames_ButKeepsTheExtension()
    {
        var sanitized = ArtifactFileName.Sanitize(new string('a', 400) + ".pdf");

        Assert.Equal(255, sanitized.Length);
        Assert.EndsWith(".pdf", sanitized, StringComparison.Ordinal);
    }
}
