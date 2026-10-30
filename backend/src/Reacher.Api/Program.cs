using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Reacher.Api;
using Reacher.Api.Codes;
using Reacher.Api.Companies;
using Reacher.Api.Searches;
using Reacher.Api.Websites;
using Reacher.Data;
using Reacher.Ingestion.Bolagsverket;
using Reacher.Ingestion.Jobs;
using Reacher.Ingestion.Scb;
using Reacher.WebsiteAnalysis;
using Reacher.WebsiteAnalysis.Brave;

var builder = WebApplication.CreateBuilder(args);

// Fail at startup with the setting's name, never its value (hard rule 6).
var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Missing setting 'ConnectionStrings:Default'.");
}

builder.Services.AddReacherOptions(builder.Configuration);
builder.Services.AddBraveSearch();
builder.Services.AddWebsiteAnalysis();

builder.Services.AddScbClient();
builder.Services.AddBolagsverketClient();
builder.Services.AddScoped<ScbIngestService>();
builder.Services.AddSingleton<IxbrlParser>();
builder.Services.AddScoped<FinancialsIngestService>();
builder.Services.AddIngestJobs();

// Public code lists, cached. Not Brave data.
builder.Services.AddMemoryCache();
builder.Services.AddScoped<ICodeTables, ScbCodeTables>();
builder.Services.AddScoped<ICodeLookup, ScbCodeLookup>();

// The fake is for the frontend. Production and the default leave this false and use
// IngestCoordinator, which writes a real ingest_job row.
if (builder.Configuration.GetValue<bool>("Ingest:UseFakeCoordinator"))
{
    var workers = builder.Services
        .Where(descriptor => descriptor.ImplementationType == typeof(IngestWorker))
        .ToList();
    foreach (var worker in workers)
    {
        builder.Services.Remove(worker);
    }

    builder.Services.AddSingleton<FakeIngestCoordinator>();
    builder.Services.AddSingleton<IIngestCoordinator>(
        services => services.GetRequiredService<FakeIngestCoordinator>());
    builder.Services.AddSingleton<IIngestJobReader>(
        services => services.GetRequiredService<FakeIngestCoordinator>());
}
else
{
    builder.Services.AddScoped<IIngestJobReader, IngestJobReader>();
}

builder.Services.AddDbContext<ReacherDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<ICompanyQueryService, CompanyQueryService>();
builder.Services.AddScoped<IWebsiteCandidateService, WebsiteCandidateService>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<ReacherDbContext>("database");

// Enums are numbers unless a converter is registered. Confidence must be
// "high", "medium", or "low" on the wire and in the OpenAPI schema.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});
builder.Services.AddOpenApi();

var app = builder.Build();

// Development only. Tests turn it off so they can start the API without a database.
if (app.Environment.IsDevelopment()
    && app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ReacherDbContext>();
    await db.Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health");
app.MapCodeEndpoints();
app.MapCompanyEndpoints();
app.MapSearchEndpoints();
app.MapWebsiteEndpoints();


app.Run();
