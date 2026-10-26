namespace Gisr.Shared.Notifications;

public sealed record NotificationListDto(long UnreadCount, IReadOnlyList<NotificationDto> Items);

