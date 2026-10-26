namespace Gisr.Shared.Affiliations;

public sealed record FieldDefinitionDto(
    string Key,
    string Label,
    string InputType,
    bool Required,
    int? MaxLength = null,
    IReadOnlyList<string>? Options = null);
