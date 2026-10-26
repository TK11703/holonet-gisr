using Gisr.Api.Common;
using Gisr.Shared.Notifications;

namespace Gisr.Api.Notifications;

public static class NotificationEndpoints
{
    public static IServiceCollection AddNotificationServices(this IServiceCollection services) =>
        services.AddScoped<NotificationService>();

    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/notifications")
            .WithTags("Notifications")
            .AddEndpointFilter<ValidationEndpointFilter>();

        group.MapGet("/", async ([AsParameters] NotificationListQuery query, NotificationService notifications, CancellationToken cancellationToken) =>
                TypedResults.Ok(await notifications.ListAsync(query, cancellationToken)))
            .WithName("listNotifications")
            .Produces<NotificationListDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem();

        group.MapPost("/{notificationId:guid}/read", async (Guid notificationId, NotificationService notifications, CancellationToken cancellationToken) =>
            {
                await notifications.MarkReadAsync(notificationId, cancellationToken);
                return TypedResults.NoContent();
            })
            .WithName("markNotificationRead")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/read-all", async (NotificationService notifications, CancellationToken cancellationToken) =>
            {
                await notifications.MarkAllReadAsync(cancellationToken);
                return TypedResults.NoContent();
            })
            .WithName("markAllNotificationsRead")
            .Produces(StatusCodes.Status204NoContent);

        return endpoints;
    }
}
