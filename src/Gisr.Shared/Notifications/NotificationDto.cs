namespace Gisr.Shared.Notifications;

public sealed record NotificationDto(Guid Id, string Kind, string Message, Guid? RecordId, DateTimeOffset CreatedAt, bool IsRead);

