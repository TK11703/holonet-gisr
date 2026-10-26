using Gisr.Api.Common;
using Gisr.Shared.Accounts;

namespace Gisr.Api.Accounts;

public static class AccountEndpoints
{
    public static IServiceCollection AddAccountServices(this IServiceCollection services)
    {
        services.AddScoped<AccountService>();
        services.AddScoped<IGraphTokenProvider, MicrosoftIdentityGraphTokenProvider>();
        services.AddHttpClient<ITenantDirectory, GraphTenantDirectory>(client =>
            client.BaseAddress = new Uri("https://graph.microsoft.com/v1.0/"));
        return services;
    }

    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/me")
            .WithTags("Accounts")
            .AddEndpointFilter<ValidationEndpointFilter>();

        group.MapGet("/", async (AccountService accounts, CancellationToken cancellationToken) =>
                TypedResults.Ok(await accounts.GetMeAsync(cancellationToken)))
            .WithName("getMe")
            .WithSummary("Current account (created on first call) and credential state")
            .Produces<AccountDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPut("/settings", async (UpdateAccountSettingsRequest request, AccountService accounts, CancellationToken cancellationToken) =>
                TypedResults.Ok(await accounts.UpdateSettingsAsync(request, cancellationToken)))
            .WithName("updateMySettings")
            .Produces<AccountDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        endpoints.MapGroup("/api/accounts")
            .WithTags("Accounts")
            .AddEndpointFilter<ValidationEndpointFilter>()
            .MapGet("/", async ([AsParameters] AccountSearchQuery query, AccountService accounts, CancellationToken cancellationToken) =>
                TypedResults.Ok(await accounts.SearchAsync(query, cancellationToken)))
            .WithName("searchAccounts")
            .WithSummary("Officer only. Search the Entra tenant directory by name or email for the record subject picker.")
            .Produces<IReadOnlyList<DirectoryUserDto>>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }
}
