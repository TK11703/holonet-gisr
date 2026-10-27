using Gisr.Api.Common;
using Gisr.Shared.Accounts;

namespace Gisr.Api.Accounts;

public static class AccountEndpoints
{
    public static IServiceCollection AddAccountServices(this IServiceCollection services)
    {
        services.AddScoped<AccountService>();
        services.AddScoped<ChainCodeService>();
        services.AddScoped<AccountAffiliationService>();
        services.AddScoped<IGraphTokenProvider, MicrosoftIdentityGraphTokenProvider>();
        services.AddScoped<IAccountIdentityTokenProvider, MicrosoftIdentityGraphTokenProvider>();
        services.AddHttpClient<IAccountIdentityDirectory, GraphAccountIdentity>(client =>
        {
            client.BaseAddress = new Uri("https://graph.microsoft.com/v1.0/");
            client.Timeout = TimeSpan.FromSeconds(5);
        });
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

        group.MapGet("/chain-code", async (ChainCodeService profiles, CancellationToken cancellationToken) =>
                TypedResults.Ok(await profiles.GetAsync(cancellationToken)))
            .WithName("getMyChainCode")
            .WithSummary("Current user's optional fictional Chain Code profile")
            .Produces<ChainCodeProfileDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/chain-code", async (UpdateChainCodeRequest request, ChainCodeService profiles, CancellationToken cancellationToken) =>
                TypedResults.Ok(await profiles.SaveAsync(request, cancellationToken)))
            .WithName("saveMyChainCode")
            .WithSummary("Save fictional details and generate a Chain Code")
            .Produces<ChainCodeProfileDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapDelete("/chain-code", async (ChainCodeService profiles, CancellationToken cancellationToken) =>
            {
                await profiles.DeleteAsync(cancellationToken);
                return TypedResults.NoContent();
            })
            .WithName("deleteMyChainCode")
            .WithSummary("Delete the current user's fictional profile and code")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/affiliation-options", () => TypedResults.Ok(AffiliationOptionCatalog.Groups))
            .WithName("listAffiliationOptions")
            .WithSummary("Searchable affiliation names grouped by category")
            .Produces<IReadOnlyList<AffiliationOptionGroupDto>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/affiliations", async (AccountAffiliationService service, CancellationToken cancellationToken) =>
                TypedResults.Ok(await service.ListAsync(cancellationToken)))
            .WithName("listMyAffiliations")
            .Produces<IReadOnlyList<AccountAffiliationDto>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/affiliations", async (AddAccountAffiliationRequest request, AccountAffiliationService service, CancellationToken cancellationToken) =>
            {
                var created = await service.AddAsync(request, cancellationToken);
                return TypedResults.Created($"/api/me/affiliations/{created.Id}", created);
            })
            .WithName("addMyAffiliation")
            .Produces<AccountAffiliationDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/affiliations/{id:guid}", async (Guid id, AccountAffiliationService service, CancellationToken cancellationToken) =>
            {
                await service.DeleteAsync(id, cancellationToken);
                return TypedResults.NoContent();
            })
            .WithName("deleteMyAffiliation")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

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
