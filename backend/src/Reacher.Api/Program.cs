using Microsoft.EntityFrameworkCore;
using Reacher.Api;
using Reacher.Data;
using Reacher.WebsiteAnalysis;

var builder = WebApplication.CreateBuilder(args);

// Fail at startup with the setting's name, never its value (hard rule 6).
var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Missing setting 'ConnectionStrings:Default'.");
}

builder.Services.AddReacherOptions(builder.Configuration);
builder.Services.AddWebsiteAnalysis();

builder.Services.AddDbContext<ReacherDbContext>(options => options.UseNpgsql(connectionString));

builder.Services.AddHealthChecks()
    .AddDbContextCheck<ReacherDbContext>("database");

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health");

app.Run();
