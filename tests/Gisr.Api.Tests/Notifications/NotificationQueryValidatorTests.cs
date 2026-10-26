using FluentValidation.TestHelper;
using Gisr.Api.Accounts;
using Gisr.Api.Notifications;

namespace Gisr.Api.Tests.Notifications;

public class NotificationQueryValidatorTests
{
    private readonly AccountSearchQueryValidator _accounts = new();
    private readonly NotificationListQueryValidator _notifications = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a")]
    [InlineData(" a ")]
    public void AccountSearch_QueryShorterThan2AfterTrimming_IsInvalid(string? query) =>
        _accounts.TestValidate(new AccountSearchQuery(query, null)).ShouldHaveValidationErrorFor(q => q.Query);

    [Theory]
    [InlineData("ab")]
    [InlineData(" ab ")]
    public void AccountSearch_QueryOf2OrMore_IsValid(string query) =>
        _accounts.TestValidate(new AccountSearchQuery(query, null)).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void AccountSearch_QueryLengthLimitIs100()
    {
        _accounts.TestValidate(new AccountSearchQuery(new string('x', 100), null)).ShouldNotHaveAnyValidationErrors();
        _accounts.TestValidate(new AccountSearchQuery(new string('x', 101), null)).ShouldHaveValidationErrorFor(q => q.Query);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(26)]
    public void AccountSearch_PageSizeMustBeBetween1And25(int pageSize) =>
        _accounts.TestValidate(new AccountSearchQuery("ab", pageSize)).ShouldHaveValidationErrorFor(q => q.PageSize);

    [Theory]
    [InlineData(1)]
    [InlineData(25)]
    public void AccountSearch_PageSizeBoundariesAreValid(int pageSize) =>
        _accounts.TestValidate(new AccountSearchQuery("ab", pageSize)).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void NotificationList_AllOmitted_IsValid() =>
        _notifications.TestValidate(new NotificationListQuery(null, null)).ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public void NotificationList_PageSizeMustBeBetween1And50(int pageSize) =>
        _notifications.TestValidate(new NotificationListQuery(null, pageSize)).ShouldHaveValidationErrorFor(q => q.PageSize);

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    public void NotificationList_PageSizeBoundariesAreValid(int pageSize) =>
        _notifications.TestValidate(new NotificationListQuery(true, pageSize)).ShouldNotHaveAnyValidationErrors();
}
