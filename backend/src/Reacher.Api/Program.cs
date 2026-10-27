using Microsoft.EntityFrameworkCore;
using Reacher.Data;

var builder = WebApplication.CreateBuilder(args);

// Fail at startup with the setting's name, never its value (hard rule 6).
// J-03 replaces this with validated options.
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Missing setting 'ConnectionStrings:Default'.");

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
