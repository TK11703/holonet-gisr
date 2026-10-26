using FluentValidation.TestHelper;
using Gisr.Api.Records;
using Gisr.Shared.Records;

namespace Gisr.Api.Tests.Records;

public class RecordValidatorTests
{
    private readonly CreateRecordRequestValidator _create = new();
    private readonly UpdateRecordRequestValidator _update = new();

    private static Dictionary<string, string> MilitaryDetail() =>
        new() { ["summary"] = "Served with distinction.", ["branch"] = "Navy", ["rank"] = "Captain" };

    private static CreateRecordRequest Create(
        string title = "Clone Wars service",
        string affiliation = "military",
        Dictionary<string, string>? detail = null,
        DateOnly? start = null,
        DateOnly? end = null,
        IReadOnlyList<IncidentDto>? incidents = null,
        IReadOnlyList<AwardDto>? awards = null) =>
        new(title, affiliation, start, end, detail ?? MilitaryDetail(), incidents, awards);

    private static IncidentDto Incident(string title = "Battle", DateOnly? on = null, string? description = null) =>
        new(null, on ?? new DateOnly(2020, 1, 1), title, description);

    private static AwardDto Award(string name = "Medal", DateOnly? on = null, string? issuer = null, string? description = null) =>
        new(null, name, on ?? new DateOnly(2021, 1, 1), issuer, description);

    [Fact]
    public void ValidRequest_Passes() => _create.TestValidate(Create()).ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Title_BlankIsInvalid(string title) =>
        _create.TestValidate(Create(title: title)).ShouldHaveValidationErrorFor(r => r.Title);

    [Theory]
    [InlineData(1)]
    [InlineData(200)]
    public void Title_BoundaryLengthsAreValid(int length) =>
        _create.TestValidate(Create(title: new string('t', length))).ShouldNotHaveValidationErrorFor(r => r.Title);

    [Fact]
    public void Title_Over200IsInvalid() =>
        _create.TestValidate(Create(title: new string('t', 201))).ShouldHaveValidationErrorFor(r => r.Title);

    [Theory]
    [InlineData("")]
    [InlineData("sith")]
    [InlineData("Military")]
    public void Affiliation_MustBeInTheCatalog(string affiliation) =>
        _create.TestValidate(Create(affiliation: affiliation)).ShouldHaveValidationErrorFor(r => r.Affiliation);

    [Fact]
    public void EngagementDetail_SummaryIsRequired()
    {
        var detail = MilitaryDetail();
        detail.Remove("summary");

        _create.TestValidate(Create(detail: detail)).ShouldHaveValidationErrorFor("EngagementDetail.summary");
    }

    [Fact]
    public void EngagementDetail_Null_ReportsRequiredFields()
    {
        var result = _create.TestValidate(Create() with { EngagementDetail = null });

        result.ShouldHaveValidationErrorFor("EngagementDetail.summary");
        result.ShouldHaveValidationErrorFor("EngagementDetail.rank");
    }

    [Fact]
    public void EngagementDetail_SummaryLimitIs4000()
    {
        var ok = MilitaryDetail();
        ok["summary"] = new string('s', 4000);
        var tooLong = MilitaryDetail();
        tooLong["summary"] = new string('s', 4001);

        _create.TestValidate(Create(detail: ok)).ShouldNotHaveValidationErrorFor("EngagementDetail.summary");
        _create.TestValidate(Create(detail: tooLong)).ShouldHaveValidationErrorFor("EngagementDetail.summary");
    }

    [Fact]
    public void EngagementDetail_RequiredFieldMustBeNonBlank()
    {
        var detail = MilitaryDetail();
        detail["rank"] = "  ";

        _create.TestValidate(Create(detail: detail)).ShouldHaveValidationErrorFor("EngagementDetail.rank");
    }

    [Fact]
    public void EngagementDetail_SelectValueMustBeAnOption()
    {
        var detail = MilitaryDetail();
        detail["branch"] = "Space Pirates";

        _create.TestValidate(Create(detail: detail)).ShouldHaveValidationErrorFor("EngagementDetail.branch");
    }

    [Fact]
    public void EngagementDetail_OptionalFieldMayBeOmittedButHasALengthLimit()
    {
        var detail = MilitaryDetail();
        detail["unit"] = new string('u', 1000);

        var result = _create.TestValidate(Create(detail: detail));

        result.ShouldHaveValidationErrorFor("EngagementDetail.unit");
        result.ShouldNotHaveValidationErrorFor("EngagementDetail.service_number");
    }

    [Fact]
    public void EngagementDetail_ValidatesAgainstTheChosenAffiliation()
    {
        var jedi = new Dictionary<string, string> { ["summary"] = "Knight of the Order", ["order_rank"] = "Knight" };

        _create.TestValidate(Create(affiliation: "jedi", detail: jedi)).ShouldNotHaveAnyValidationErrors();
        _create.TestValidate(Create(affiliation: "military", detail: jedi)).ShouldHaveValidationErrorFor("EngagementDetail.rank");
    }

    [Fact]
    public void EngagementDetail_UnknownKeysArePreserved_NotRejected()
    {
        var detail = MilitaryDetail();
        detail["mentor"] = "Qui-Gon";
        detail["legacy"] = "kept";

        _create.TestValidate(Create(detail: detail)).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void EngagementDetail_UnknownKeyValuesAreBounded()
    {
        var detail = MilitaryDetail();
        detail["legacy"] = new string('x', 4001);

        _create.TestValidate(Create(detail: detail)).ShouldHaveValidationErrorFor("EngagementDetail.legacy");
    }

    [Fact]
    public void EngagementDetail_AtMost50Entries()
    {
        var detail = MilitaryDetail();
        for (var i = 0; i < 50; i++)
        {
            detail[$"extra{i}"] = "x";
        }

        _create.TestValidate(Create(detail: detail)).ShouldHaveValidationErrorFor("EngagementDetail");
    }

    [Fact]
    public void EngagementEnd_BeforeStart_IsInvalid() =>
        _create.TestValidate(Create(start: new DateOnly(2020, 6, 2), end: new DateOnly(2020, 6, 1)))
            .ShouldHaveValidationErrorFor(r => r.EngagementEnd);

    [Fact]
    public void EngagementDates_EqualOrPartial_AreValid()
    {
        _create.TestValidate(Create(start: new DateOnly(2020, 6, 1), end: new DateOnly(2020, 6, 1))).ShouldNotHaveAnyValidationErrors();
        _create.TestValidate(Create(start: new DateOnly(2020, 6, 1))).ShouldNotHaveAnyValidationErrors();
        _create.TestValidate(Create(end: new DateOnly(2020, 6, 1))).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Incident_TitleAndDateAreRequired_DescriptionIsBounded()
    {
        var result = _create.TestValidate(Create(incidents:
        [
            Incident(title: ""),
            Incident(title: new string('t', 201)),
            Incident(on: default(DateOnly)),
            Incident(description: new string('d', 4001)),
        ]));

        result.ShouldHaveValidationErrorFor("Incidents[0].Title");
        result.ShouldHaveValidationErrorFor("Incidents[1].Title");
        result.ShouldHaveValidationErrorFor("Incidents[2].OccurredOn");
        result.ShouldHaveValidationErrorFor("Incidents[3].Description");
    }

    [Fact]
    public void Incident_BoundaryValuesAreValid() =>
        _create.TestValidate(Create(incidents: [Incident(title: new string('t', 200), description: new string('d', 4000))]))
            .ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Award_NameAndDateAreRequired_TextIsBounded()
    {
        var result = _create.TestValidate(Create(awards:
        [
            Award(name: ""),
            Award(name: new string('n', 201)),
            Award(on: default(DateOnly)),
            Award(issuer: new string('i', 201)),
            Award(description: new string('d', 2001)),
        ]));

        result.ShouldHaveValidationErrorFor("Awards[0].Name");
        result.ShouldHaveValidationErrorFor("Awards[1].Name");
        result.ShouldHaveValidationErrorFor("Awards[2].AwardedOn");
        result.ShouldHaveValidationErrorFor("Awards[3].Issuer");
        result.ShouldHaveValidationErrorFor("Awards[4].Description");
    }

    [Fact]
    public void Award_BoundaryValuesAreValid() =>
        _create.TestValidate(Create(awards: [Award(name: new string('n', 200), issuer: new string('i', 200), description: new string('d', 2000))]))
            .ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Collections_AreLimitedTo100Each()
    {
        var hundredIncidents = Enumerable.Range(0, 100).Select(i => Incident($"i{i}")).ToList();
        var hundredAwards = Enumerable.Range(0, 100).Select(i => Award($"a{i}")).ToList();

        _create.TestValidate(Create(incidents: hundredIncidents, awards: hundredAwards)).ShouldNotHaveAnyValidationErrors();
        _create.TestValidate(Create(incidents: [.. hundredIncidents, Incident()])).ShouldHaveValidationErrorFor(r => r.Incidents);
        _create.TestValidate(Create(awards: [.. hundredAwards, Award()])).ShouldHaveValidationErrorFor(r => r.Awards);
    }

    private static UpdateRecordRequest Update(int expectedVersion) =>
        new("Title", "military", null, null, MilitaryDetail(), null, null, expectedVersion);

    [Fact]
    public void Update_SharesTheCreateRules()
    {
        var result = _update.TestValidate(Update(1) with { Title = "", Affiliation = "sith" });

        result.ShouldHaveValidationErrorFor(r => r.Title);
        result.ShouldHaveValidationErrorFor(r => r.Affiliation);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Update_ExpectedVersionMustBeAtLeastOne(int version) =>
        _update.TestValidate(Update(version)).ShouldHaveValidationErrorFor(r => r.ExpectedVersion);

    [Fact]
    public void Update_ValidRequest_Passes() => _update.TestValidate(Update(3)).ShouldNotHaveAnyValidationErrors();
}
