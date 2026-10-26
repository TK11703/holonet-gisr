using System.Text.Json.Serialization;

namespace Gisr.Shared.Accounts;

[JsonConverter(typeof(JsonStringEnumConverter<ProfileColor>))]
public enum ProfileColor
{
    Black, Brown, Blue, Green, Gray, Hazel, Amber, Red, Orange, Yellow,
    Gold, Silver, White, Pink, Purple, Tan, Pale, Dark, None, Other,
}

public sealed record UpdateChainCodeRequest
{
    public string? FullName { get; init; }
    public string? BirthDate { get; init; }
    public string? Homeworld { get; init; }
    public string? Species { get; init; }
    public string? Height { get; init; }
    public ProfileColor? EyeColor { get; init; }
    public ProfileColor? SkinColor { get; init; }
    public ProfileColor? HairColor { get; init; }
    public string? DistinguishingMarks { get; init; }
    public string? FamilyHistory { get; init; }
    public string? CriminalHistory { get; init; }
    public string? Biography { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProfileColor[]? EyeColors { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProfileColor[]? SkinColors { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProfileColor[]? HairColors { get; init; }
}

public sealed record ChainCodeProfileDto(UpdateChainCodeRequest Profile, string ChainCode, DateTimeOffset UpdatedAt);