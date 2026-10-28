using Microsoft.EntityFrameworkCore;
using Reacher.Api;
using Reacher.Api.Companies;
using Reacher.Data;
using Reacher.Ingestion.Bolagsverket;
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

builder.Services.AddDbContext<ReacherDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<ICompanyQueryService, CompanyQueryService>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<ReacherDbContext>("database");

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
app.MapCompanyEndpoints();

app.Run();
