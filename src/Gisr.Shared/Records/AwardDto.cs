namespace Gisr.Shared.Records;

public sealed record AwardDto(Guid? Id, string Name, DateOnly AwardedOn, string? Issuer, string? Description);
