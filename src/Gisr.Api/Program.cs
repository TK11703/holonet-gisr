using FluentValidation;
using Gisr.Api.Accounts;
using Gisr.Api.Affiliations;
using Gisr.Api.Artifacts;
using Gisr.Api.Common;
using Gisr.Api.Gallery;
using Gisr.Api.Notifications;
using Gisr.Api.Records;
using Gisr.Api.Search;
using Gisr.Database.Common;
using Gisr.Database.Migrations;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddGisrAuth(builder.Configuration);
builder.Services.AddGisrProblemDetails();
builder.Services.AddGisrObservability(builder.Configuration, "Gisr.Api");
builder.Services.AddGisrDatabase(
    builder.Configuration.GetConnectionString("Gisr")
    ?? throw new InvalidOperationException("ConnectionStrings:Gisr is not configured."));
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddGisrArtifactStore(
    builder.Configuration.GetConnectionString("blobs"),
    builder.Configuration["Storage:BlobEndpoint"]);
builder.Services.AddAccountServices();
builder.Services.AddRecordServices();
builder.Services.AddArtifactServices();
builder.Services.AddGalleryServices();
builder.Services.AddSearchServices(builder.Configuration);
builder.Services.AddNotificationServices();
builder.Services.AddOpenApi();

var app = builder.Build();

await app.Services.GetRequiredService<MigrationRunner>().ApplyAsync(app.Lifetime.ApplicationStopping);

// Request logging wraps the exception handler so expected problems (403/404/409/503) are logged with their real status, not as 500 errors.
app.UseSerilogRequestLogging();
app.UseGisrProblemDetails();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();
app.MapAccountEndpoints();
app.MapAffiliationEndpoints();
app.MapRecordEndpoints();
app.MapArtifactEndpoints();
app.MapGalleryEndpoints();
app.MapSearchEndpoints();
app.MapNotificationEndpoints();

await app.RunAsync();

public partial class Program;
