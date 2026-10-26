using FluentValidation.TestHelper;
using Gisr.Api.Records;
using Gisr.Shared.Records;

namespace Gisr.Api.Tests.Records;

public class SealValidationTests
{
    private readonly CreateRecordRequestValidator _create = new();
    private readonly UpdateRecordRequestValidator _update = new();

    private static Dictionary<string, string> Detail() =>
        new() { ["summary"] = "Served", ["branch"] = "Navy", ["rank"] = "Captain" };

    private static CreateRecordRequest Create(bool sealedRecord = false, string? code = null) =>
        new("Title", "military", null, null, Detail(), IsSealed: sealedRecord, OverrideCode: code);

    private static UpdateRecordRequest Update(bool sealedRecord = false, string? code = null) =>
        new("Title", "military", null, null, Detail(), null, null, 1, sealedRecord, code);

    [Fact]
    public void Create_Unsealed_NeedsNoCode() =>
        _create.TestValidate(Create()).ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Sealed_RequiresACode(string? code) =>
        _create.TestValidate(Create(true, code)).ShouldHaveValidationErrorFor(r => r.OverrideCode);

    [Theory]
    [InlineData("1234567")]
    public void Create_Sealed_CodeShorterThan8_IsInvalid(string code) =>
        _create.TestValidate(Create(true, code)).ShouldHaveValidationErrorFor(r => r.OverrideCode);

    [Fact]
    public void Create_Sealed_CodeLengthBoundaries_AreValid()
    {
        _create.TestValidate(Create(true, new string('x', 8))).ShouldNotHaveAnyValidationErrors();
        _create.TestValidate(Create(true, new string('x', 64))).ShouldNotHaveAnyValidationErrors();
        _create.TestValidate(Create(true, new string('x', 65))).ShouldHaveValidationErrorFor(r => r.OverrideCode);
    }

    [Fact]
    public void Create_Sealed_ABlankCodeOfEightSpaces_IsInvalid() =>
        _create.TestValidate(Create(true, new string(' ', 8))).ShouldHaveValidationErrorFor(r => r.OverrideCode);

    [Fact]
    public void Create_CodeWithoutSealing_IsInvalid() =>
        _create.TestValidate(Create(false, "a valid code")).ShouldHaveValidationErrorFor(r => r.OverrideCode);

    [Fact]
    public void Update_Sealed_CodeIsOptional_BecauseTheServiceKnowsWhetherARecordIsAlreadySealed() =>
        _update.TestValidate(Update(true, null)).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Update_Sealed_ProvidedCode_MustBe8To64Characters()
    {
        _update.TestValidate(Update(true, "short")).ShouldHaveValidationErrorFor(r => r.OverrideCode);
        _update.TestValidate(Update(true, new string('x', 65))).ShouldHaveValidationErrorFor(r => r.OverrideCode);
        _update.TestValidate(Update(true, "long enough code")).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Update_Unsealing_ClearsTheCode_SoProvidingOneIsInvalid()
    {
        _update.TestValidate(Update(false, null)).ShouldNotHaveAnyValidationErrors();
        _update.TestValidate(Update(false, "a valid code")).ShouldHaveValidationErrorFor(r => r.OverrideCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void OverrideEntry_Empty_IsInvalid(string? code) =>
        new OverrideCodeRequestValidator().TestValidate(new OverrideCodeRequest(code!)).ShouldHaveValidationErrorFor(r => r.Code);

    [Fact]
    public void OverrideEntry_MaxLengthIs64_AndShortCodesAreAllowedBecauseTheyJustFailTheCheck()
    {
        var validator = new OverrideCodeRequestValidator();

        validator.TestValidate(new OverrideCodeRequest("x")).ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new OverrideCodeRequest(new string('x', 64))).ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new OverrideCodeRequest(new string('x', 65))).ShouldHaveValidationErrorFor(r => r.Code);
    }
}
