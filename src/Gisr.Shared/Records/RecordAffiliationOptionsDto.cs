namespace Gisr.Shared.Records;

// Distinct affiliations on the records the caller can list, for the record list's filters.
public sealed record RecordAffiliationOptionsDto(IReadOnlyList<string> Primaries, IReadOnlyList<string> Secondaries);
