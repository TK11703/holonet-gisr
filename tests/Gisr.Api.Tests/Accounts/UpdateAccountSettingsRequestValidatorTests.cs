using FluentValidation.TestHelper;
using Gisr.Api.Accounts;
using Gisr.Shared.Accounts;

namespace Gisr.Api.Tests.Accounts;

public class UpdateAccountSettingsRequestValidatorTests
{
    private readonly UpdateAccountSettingsRequestValidator _validator = new();

    private static UpdateAccountSettingsRequest Request(string name = "Ahsoka", string? email = null) =>
        new(name, email, true);

    // 64 + 1 + 63 + 1 + 63 + 1 + 57 + 4 = 254 characters
    private static string EmailOfLength254() =>
        $"{new string('a', 64)}@{new string('b', 63)}.{new string('c', 63)}.{new string('d', 57)}.com";

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void DisplayName_BlankIsInvalid(string name) =>
        _validator.TestValidate(Request(name)).ShouldHaveValidationErrorFor(r => r.DisplayName);

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void DisplayName_BoundaryLengthsAreValid(int length) =>
        _validator.TestValidate(Request(new string('x', length))).ShouldNotHaveValidationErrorFor(r => r.DisplayName);

    [Fact]
    public void DisplayName_Over100IsInvalid() =>
        _validator.TestValidate(Request(new string('x', 101))).ShouldHaveValidationErrorFor(r => r.DisplayName);

    [Fact]
    public void ContactEmail_NullIsValid() =>
        _validator.TestValidate(Request(email: null)).ShouldNotHaveValidationErrorFor(r => r.ContactEmail);

    [Fact]
    public void ContactEmail_ValidAddressIsValid() =>
        _validator.TestValidate(Request(email: "ahsoka@jedi.example")).ShouldNotHaveValidationErrorFor(r => r.ContactEmail);

    [Fact]
    public void ContactEmail_NotAnAddressIsInvalid() =>
        _validator.TestValidate(Request(email: "not-an-email")).ShouldHaveValidationErrorFor(r => r.ContactEmail);

    [Fact]
    public void ContactEmail_254CharactersIsValid()
    {
        var email = EmailOfLength254();
        Assert.Equal(254, email.Length);

        _validator.TestValidate(Request(email: email)).ShouldNotHaveValidationErrorFor(r => r.ContactEmail);
    }

    [Fact]
    public void ContactEmail_255CharactersIsInvalid()
    {
        var email = EmailOfLength254() + "m";
        Assert.Equal(255, email.Length);

        _validator.TestValidate(Request(email: email)).ShouldHaveValidationErrorFor(r => r.ContactEmail);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("teal")]
    [InlineData("hub")]
    [InlineData("navy")]
    [InlineData("grey")]
    public void Theme_NullOrKnownIsValid(string? theme) =>
        _validator.TestValidate(Request() with { Theme = theme }).ShouldNotHaveValidationErrorFor(r => r.Theme);

    [Theory]
    [InlineData("")]
    [InlineData("Hub")]
    [InlineData("purple")]
    [InlineData("hub; drop table account")]
    public void Theme_UnknownIsInvalid(string theme) =>
        _validator.TestValidate(Request() with { Theme = theme }).ShouldHaveValidationErrorFor(r => r.Theme);
}
