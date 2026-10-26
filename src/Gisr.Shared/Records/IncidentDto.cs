namespace Gisr.Shared.Records;

public sealed record IncidentDto(Guid? Id, DateOnly OccurredOn, string Title, string? Description);
