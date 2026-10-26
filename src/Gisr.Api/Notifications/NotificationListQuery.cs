using FluentValidation;

namespace Gisr.Api.Notifications;

public sealed record NotificationListQuery(bool? UnreadOnly, int? PageSize);

public sealed class NotificationListQueryValidator : AbstractValidator<NotificationListQuery>
{
    public NotificationListQueryValidator() => RuleFor(q => q.PageSize).InclusiveBetween(1, 50);
}
