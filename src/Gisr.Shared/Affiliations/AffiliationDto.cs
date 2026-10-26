namespace Gisr.Shared.Affiliations;

public sealed record AffiliationDto(string Code, string Name, IReadOnlyList<FieldDefinitionDto> Fields);
