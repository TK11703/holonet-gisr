using Gisr.Api.Accounts;
using Gisr.Shared.Accounts;
using System.Text.Json;

namespace Gisr.Api.Tests.Accounts;

public class ChainCodeServiceTests
{
    public static IEnumerable<object[]> ChangedFields() => new UpdateChainCodeRequest[]
    {
        new() { FullName = "Leia" }, new() { BirthDate = "19 BBY" }, new() { Homeworld = "Alderaan" },
        new() { Species = "Human" }, new() { Height = "150 cm" }, new() { EyeColor = ProfileColor.Brown },
        new() { SkinColor = ProfileColor.Pale }, new() { HairColor = ProfileColor.Brown },
        new() { DistinguishingMarks = "Scar" }, new() { FamilyHistory = "Organa" },
        new() { CriminalHistory = "Rebel" }, new() { Biography = "Senator" },
    }.Select(profile => new object[] { profile });

    [Theory]
    [MemberData(nameof(ChangedFields))]
    public void Generate_EveryFieldContributes(UpdateChainCodeRequest changed)
    {
        var owner = Guid.NewGuid();
        var baseline = ChainCodeService.Generate(owner, new UpdateChainCodeRequest());
        var code = ChainCodeService.Generate(owner, changed);
        Assert.Matches(@"^\d{5}(-\d{5}){7}$", code);
        Assert.NotEqual(baseline, code);
        Assert.Equal(code, ChainCodeService.Generate(owner, changed));
    }

    [Fact]
    public void Generate_ColorSetsAreOrderIndependent_AndPreserveLegacySingleColorCodes()
    {
        var owner = Guid.NewGuid();
        foreach (var (collection, scalar) in new[] { ("eyeColors", "eyeColor"), ("skinColors", "skinColor"), ("hairColors", "hairColor") })
        {
            var colors = ColorRequest(collection, new[] { "Blue", "Green" });
            var reordered = ColorRequest(collection, new[] { "Green", "Blue", "Green" });
            var changed = ColorRequest(collection, new[] { "Blue", "Brown" });
            Assert.Equal(ChainCodeService.Generate(owner, colors), ChainCodeService.Generate(owner, reordered));
            Assert.NotEqual(ChainCodeService.Generate(owner, colors), ChainCodeService.Generate(owner, changed));
            Assert.Equal(ChainCodeService.Generate(owner, ColorRequest(scalar, "Blue")),
                ChainCodeService.Generate(owner, ColorRequest(collection, new[] { "Blue" })));
            Assert.Equal(ChainCodeService.Generate(owner, new UpdateChainCodeRequest()),
                ChainCodeService.Generate(owner, ColorRequest(collection, Array.Empty<string>())));
        }
    }

    [Theory]
    [InlineData("eyeColors")]
    [InlineData("skinColors")]
    [InlineData("hairColors")]
    public void Validator_RejectsInvalidOrOversizedColorSets(string collection)
    {
        var validator = new UpdateChainCodeRequestValidator();
        Assert.False(validator.Validate(ColorRequest(collection, new[] { 999 }) with { FullName = "Leia" }).IsValid);
        Assert.False(validator.Validate(ColorRequest(collection, Enumerable.Repeat("Blue", 21).ToArray()) with { FullName = "Leia" }).IsValid);
        Assert.True(validator.Validate(ColorRequest(collection, new[] { "Blue", "Green" })).IsValid);
    }

    private static UpdateChainCodeRequest ColorRequest(string property, object value) =>
        JsonSerializer.Deserialize<UpdateChainCodeRequest>(JsonSerializer.Serialize(new Dictionary<string, object> { [property] = value }),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    [Fact]
    public void Generate_NormalizesAllText_AndSeparatesOwners()
    {
        var owner = Guid.NewGuid();
        var canonical = new UpdateChainCodeRequest
        {
            FullName = "Padm\u00e9", BirthDate = "46 BBY", Homeworld = "Naboo", Species = "Human", Height = "165 cm",
            DistinguishingMarks = "None\nKnown", FamilyHistory = "Naberrie\nFamily", CriminalHistory = "None\nRecorded", Biography = "Queen\nSenator",
        };
        var equivalent = canonical with
        {
            FullName = "  Padme\u0301 ", BirthDate = " 46 BBY ", Homeworld = " Naboo ", Species = " Human ", Height = " 165 cm ",
            DistinguishingMarks = " None\r\nKnown ", FamilyHistory = " Naberrie\rFamily ",
            CriminalHistory = " None\r\nRecorded ", Biography = " Queen\r\nSenator ",
        };
        Assert.Equal(canonical, ChainCodeService.Normalize(equivalent));
        Assert.Equal(ChainCodeService.Generate(owner, canonical), ChainCodeService.Generate(owner, equivalent));
        Assert.NotEqual(ChainCodeService.Generate(owner, canonical), ChainCodeService.Generate(Guid.NewGuid(), canonical));
        Assert.Equal(new UpdateChainCodeRequest(), ChainCodeService.Normalize(new UpdateChainCodeRequest { Biography = " \r\n\t" }));
    }
}