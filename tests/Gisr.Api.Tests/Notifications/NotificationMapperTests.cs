using System.Reflection;
using Gisr.Api.Notifications;
using Gisr.Database.Notifications;
using Gisr.Shared.Notifications;

namespace Gisr.Api.Tests.Notifications;

public class NotificationMapperTests
{
    private static NotificationRow Row(DateTimeOffset? readAt = null) => new()
    {
        Id = Guid.NewGuid(),
        RecipientAccountId = Guid.NewGuid(),
        Kind = NotificationKind.ServiceRecordCreated,
        RecordId = Guid.NewGuid(),
        Message = "Officer created a service record for you.",
        CreatedAt = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero),
        ReadAt = readAt,
    };

    [Fact]
    public void ToDto_MapsTheRow()
    {
        var row = Row();

        var dto = NotificationMapper.ToDto(row);

        Assert.Equal(row.Id, dto.Id);
        Assert.Equal("service_record_created", dto.Kind);
        Assert.Equal(row.Message, dto.Message);
        Assert.Equal(row.RecordId, dto.RecordId);
        Assert.Equal(row.CreatedAt, dto.CreatedAt);
    }

    [Fact]
    public void IsRead_FollowsReadAt()
    {
        Assert.False(NotificationMapper.ToDto(Row()).IsRead);
        Assert.True(NotificationMapper.ToDto(Row(DateTimeOffset.UtcNow)).IsRead);
    }

    [Fact]
    public void Dto_ExposesNoRecordContentOrRecipient()
    {
        var names = typeof(NotificationDto).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).Order().ToArray();

        Assert.Equal(["CreatedAt", "Id", "IsRead", "Kind", "Message", "RecordId"], names);
    }

    [Fact]
    public void ToList_CarriesTheUnreadCount()
    {
        var list = NotificationMapper.ToListDto(new NotificationList(5, [Row(), Row(DateTimeOffset.UtcNow)]));

        Assert.Equal(5, list.UnreadCount);
        Assert.Equal(2, list.Items.Count);
    }
}
