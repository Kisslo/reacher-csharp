using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Reacher.Api.Tests;

internal static class ReacherApiFactory
{
    /// <summary>Not a real credential. Present so startup validation can pass.</summary>
    private const string Placeholder = "test-only";

    /// <summary>
    /// A value that must never appear in a startup error. Proves the message names
    /// the missing setting and does not echo a configured secret.
    /// </summary>
    internal const string Sentinel = "sentinel-do-not-log";

    /// <summary>Nothing listens on port 1, so connecting fails at once.</summary>
    public const string UnreachableDatabase =
        "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=2";

    /// <summary>Runs the real Program.cs in memory against the given database.</summary>
    public static WebApplicationFactory<Program> Create(string connectionString) =>
        CreateCore(connectionString, omitSetting: null);

    /// <summary>
    /// Same as <see cref="Create(string)"/>, but one required setting is blank so
    /// startup validation fails.
    /// </summary>
    public static WebApplicationFactory<Program> Create(
        string connectionString,
        string omitSetting) =>
        CreateCore(connectionString, omitSetting);

    private static WebApplicationFactory<Program> CreateCore(
        string connectionString,
        string? omitSetting)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = connectionString,
            ["Scb:ApiKey"] = Placeholder,
            ["Bolagsverket:ClientId"] = Placeholder,
            ["Bolagsverket:ClientSecret"] = Placeholder,
            ["Brave:ApiKey"] = Placeholder,
            ["PageSpeed:ApiKey"] = Placeholder,
            // Some tests point at a database that does not exist.
            ["Database:MigrateOnStartup"] = "false",
        };

        if (omitSetting is not null)
        {
            settings[omitSetting] = "";
            if (omitSetting != "Brave:ApiKey")
            {
                settings["Brave:ApiKey"] = Sentinel;
            }

            if (omitSetting == "Brave:ApiKey")
            {
                settings["Brave:UseFake"] = "false";
            }
        }

        // UseSetting writes straight onto the configuration manager, so these values
        // win over appsettings and any user-secrets on this machine. An empty string
        // still counts: only null is ignored. Placeholders are not real keys.
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        });
    }
}
