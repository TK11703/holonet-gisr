using FluentValidation.TestHelper;
using Gisr.Api.Records;

namespace Gisr.Api.Tests.Records;

public class RecordQueryValidatorTests
{
    private readonly RecordListQueryValidator _list = new();
    private readonly HistoryQueryValidator _history = new();

    [Fact]
    public void List_AllOmitted_IsValid() =>
        _list.TestValidate(new RecordListQuery(null, null, null, null, null)).ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData("mine")]
    [InlineData("all")]
    public void List_ScopeAcceptsMineAndAll(string scope) =>
        _list.TestValidate(new RecordListQuery(scope, null, null, null, null)).ShouldNotHaveValidationErrorFor(q => q.Scope);

    [Theory]
    [InlineData("")]
    [InlineData("everything")]
    [InlineData("Mine")]
    public void List_ScopeRejectsOtherValues(string scope) =>
        _list.TestValidate(new RecordListQuery(scope, null, null, null, null)).ShouldHaveValidationErrorFor(q => q.Scope);

    [Theory]
    [InlineData("Military")]
    [InlineData("Jedi Order \u203A Knight")]
    public void List_AffiliationAcceptsAnyText(string affiliation) =>
        _list.TestValidate(new RecordListQuery(null, affiliation, null, null, null)).ShouldNotHaveValidationErrorFor(q => q.Affiliation);

    [Fact]
    public void List_AffiliationRejectsBlankAndOverlongText()
    {
        _list.TestValidate(new RecordListQuery(null, "", null, null, null)).ShouldHaveValidationErrorFor(q => q.Affiliation);
        _list.TestValidate(new RecordListQuery(null, new string('a', 321), null, null, null)).ShouldHaveValidationErrorFor(q => q.Affiliation);
    }

    [Theory]
    [InlineData("updatedDesc")]
    [InlineData("updatedAsc")]
    [InlineData("titleAsc")]
    [InlineData("titleDesc")]
    public void List_SortAcceptsTheDocumentedValues(string sort) =>
        _list.TestValidate(new RecordListQuery(null, null, sort, null, null)).ShouldNotHaveValidationErrorFor(q => q.Sort);

    [Theory]
    [InlineData("newest")]
    [InlineData("")]
    [InlineData("TitleAsc")]
    public void List_SortRejectsOtherValues(string sort) =>
        _list.TestValidate(new RecordListQuery(null, null, sort, null, null)).ShouldHaveValidationErrorFor(q => q.Sort);

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void List_PageMustBeAtLeastOne(int page) =>
        _list.TestValidate(new RecordListQuery(null, null, null, page, null)).ShouldHaveValidationErrorFor(q => q.Page);

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void List_PageSizeMustBeBetween1And100(int pageSize) =>
        _list.TestValidate(new RecordListQuery(null, null, null, null, pageSize)).ShouldHaveValidationErrorFor(q => q.PageSize);

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void List_PageSizeBoundariesAreValid(int pageSize) =>
        _list.TestValidate(new RecordListQuery(null, null, null, 1, pageSize)).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void List_SubjectFilter_IsOnlyValidWithScopeAll()
    {
        var subject = Guid.NewGuid();

        _list.TestValidate(new RecordListQuery("all", null, null, null, null, subject)).ShouldNotHaveAnyValidationErrors();
        _list.TestValidate(new RecordListQuery("mine", null, null, null, null, subject)).ShouldHaveValidationErrorFor(q => q.SubjectAccountId);
        _list.TestValidate(new RecordListQuery(null, null, null, null, null, subject)).ShouldHaveValidationErrorFor(q => q.SubjectAccountId);
    }

    [Fact]
    public void History_OmittedIsValid() =>
        _history.TestValidate(new HistoryQuery(null, null)).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void History_PageMustBeAtLeastOne() =>
        _history.TestValidate(new HistoryQuery(0, null)).ShouldHaveValidationErrorFor(q => q.Page);

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void History_PageSizeMustBeBetween1And100(int pageSize) =>
        _history.TestValidate(new HistoryQuery(null, pageSize)).ShouldHaveValidationErrorFor(q => q.PageSize);

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void History_PageSizeBoundariesAreValid(int pageSize) =>
        _history.TestValidate(new HistoryQuery(2, pageSize)).ShouldNotHaveAnyValidationErrors();
}
