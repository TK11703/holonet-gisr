using Gisr.Api.Accounts;
using Gisr.Api.Common;
using Gisr.Database.Accounts;
using Gisr.Database.Notifications;
using Gisr.Shared.Notifications;

namespace Gisr.Api.Notifications;

public sealed class NotificationService(NotificationRepository notifications, AccountService accounts)
{
    private const int DefaultPageSize = 20;
    public const string SealedRecordMessage = "A sealed record was added to your file";

    // The message names the author only; record content never goes into a notification, and a sealed record gets fixed text.
    public static NotificationWrite? ForServiceRecord(AccountRow subject, AccountRow author, bool isSealed = false) =>
        subject.Id == author.Id || !subject.NotifyOnServiceRecord
            ? null
            : new NotificationWrite(
                subject.Id,
                NotificationKind.ServiceRecordCreated,
                null,
                isSealed ? SealedRecordMessage : $"{author.DisplayName} created a service record for you.");

    public async Task<NotificationListDto> ListAsync(NotificationListQuery query, CancellationToken cancellationToken)
    {
        var account = await accounts.EnsureAccountAsync(cancellationToken);
        var list = await notifications.ListAsync(account.Id, query.UnreadOnly ?? false, query.PageSize ?? DefaultPageSize, cancellationToken);
        return NotificationMapper.ToListDto(list);
    }

    public async Task MarkReadAsync(Guid notificationId, CancellationToken cancellationToken)
    {
        var account = await accounts.EnsureAccountAsync(cancellationToken);
        if (!await notifications.MarkReadAsync(notificationId, account.Id, cancellationToken))
        {
            throw new ProblemException(StatusCodes.Status404NotFound, "Not Found");
        }
    }

    public async Task MarkAllReadAsync(CancellationToken cancellationToken)
    {
        var account = await accounts.EnsureAccountAsync(cancellationToken);
        await notifications.MarkAllReadAsync(account.Id, cancellationToken);
    }
}
