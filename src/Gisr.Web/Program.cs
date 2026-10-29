using Gisr.Web.Auth;
using Gisr.Web.Artifacts;
using Gisr.Web.Common;
using Gisr.Web.Components;
using Gisr.Web.Layout;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, _, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.Configure<ApiOptions>(builder.Configuration.GetSection(ApiOptions.SectionName));
var apiScope = builder.Configuration[$"{ApiOptions.SectionName}:Scope"];

builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"))
    .EnableTokenAcquisitionToCallDownstreamApi(string.IsNullOrWhiteSpace(apiScope) ? [] : [apiScope])
    .AddInMemoryTokenCaches();
builder.Services.RejectSessionsMissingFromTokenCache(apiScope ?? string.Empty);

builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddScoped<IAccessTokenProvider, EntraAccessTokenProvider>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ShellInterop>();
builder.Services.AddScoped<ThemeInterop>();
builder.Services.AddScoped<CurrentUserState>();
builder.Services.AddSingleton<Gisr.Web.Records.ViewGrantStore>();
builder.Services.AddHttpClient<ApiClient>((_, client) =>
    client.BaseAddress = new Uri(
        builder.Configuration[$"{ApiOptions.SectionName}:BaseUrl"]
        ?? throw new InvalidOperationException("Api:BaseUrl is not configured.")));
builder.Services.AddGisrObservability(builder.Configuration, "Gisr.Web");

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseSerilogRequestLogging();
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets().AllowAnonymous();
app.MapAuthEndpoints();
app.MapMediaEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.RunAsync();

public partial class Program;
