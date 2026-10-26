using Gisr.Api.Notifications;
using Gisr.Database.Accounts;
using Gisr.Database.Notifications;

namespace Gisr.Api.Tests.Notifications;

public class NotificationServiceTests
{
    private static AccountRow Account(string name, bool notify = true) => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = name,
        NotifyOnServiceRecord = notify,
    };

    [Fact]
    public void ServiceRecord_CreatesOneNotificationForTheSubject()
    {
        var subject = Account("Citizen");
        var officer = Account("Officer Cody");

        var notification = NotificationService.ForServiceRecord(subject, officer);

        Assert.NotNull(notification);
        Assert.Equal(subject.Id, notification.RecipientAccountId);
        Assert.Equal(NotificationKind.ServiceRecordCreated, notification.Kind);
        Assert.Null(notification.RecordId);
    }

    [Fact]
    public void Message_NamesTheAuthor_AndCarriesNoRecordContent()
    {
        var notification = NotificationService.ForServiceRecord(Account("Citizen"), Account("Officer Cody"))!;

        Assert.Equal("Officer Cody created a service record for you.", notification.Message);
    }

    [Fact]
    public void NoNotification_WhenTheSubjectTurnedThemOff() =>
        Assert.Null(NotificationService.ForServiceRecord(Account("Citizen", notify: false), Account("Officer")));

    [Fact]
    public void NoNotification_ForSelfAuthoredRecords()
    {
        var citizen = Account("Citizen");

        Assert.Null(NotificationService.ForServiceRecord(citizen, citizen));
        Assert.Null(NotificationService.ForServiceRecord(citizen, new AccountRow { Id = citizen.Id, DisplayName = "Same id" }));
    }
}
