using Gisr.Database.Notifications;
using Gisr.Shared.Notifications;

namespace Gisr.Api.Notifications;

public static class NotificationMapper
{
    public static NotificationDto ToDto(NotificationRow row) =>
        new(row.Id, row.Kind, row.Message, row.RecordId, row.CreatedAt, row.ReadAt is not null);

    public static NotificationListDto ToListDto(NotificationList list) =>
        new(list.UnreadCount, [.. list.Items.Select(ToDto)]);
}
