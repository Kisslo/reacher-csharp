using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Internal;
using Reacher.Api.Codes;
using Reacher.Ingestion.Scb;

namespace Reacher.Api.Tests;

public sealed class ScbCodeTablesTests
{
    [Fact]
    public async Task Municipalities_are_sorted_in_Swedish_order()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var scb = new RecordingScbClient();
        scb.Tables[ScbCodeTables.MunicipalityTableName] =
        [
            new CodeItem("1880", "Örebro"),
            new CodeItem("1283", "Ängelholm"),
            new CodeItem("2321", "Åre"),
            new CodeItem("1984", "Arboga"),
            new CodeItem("1285", "Ystad"),
        ];
        var tables = new ScbCodeTables(scb, cache);

        var municipalities = await tables.GetMunicipalitiesAsync(CancellationToken.None);

        Assert.Equal(
            ["Arboga", "Ystad", "Åre", "Ängelholm", "Örebro"],
            municipalities.Select(row => row.Name));
    }

    [Fact]
    public async Task Municipality_codes_get_their_leading_zero_and_names_are_trimmed()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var scb = new RecordingScbClient();
        scb.Tables[ScbCodeTables.MunicipalityTableName] = [new CodeItem("180", " Stockholm ")];
        var tables = new ScbCodeTables(scb, cache);

        var municipalities = await tables.GetMunicipalitiesAsync(CancellationToken.None);

        Assert.Equal(new CodeResponse("0180", "Stockholm"), Assert.Single(municipalities));
    }

    [Fact]
    public async Task Rows_without_a_code_or_a_name_are_left_out()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var scb = new RecordingScbClient();
        scb.Tables[ScbCodeTables.MunicipalityTableName] =
        [
            new CodeItem("0180", "Stockholm"),
            new CodeItem(null, "Ingen kod"),
            new CodeItem(" ", "Tom kod"),
            new CodeItem("0184", null),
            new CodeItem("1480", " "),
        ];
        var tables = new ScbCodeTables(scb, cache);

        var municipalities = await tables.GetMunicipalitiesAsync(CancellationToken.None);

        Assert.Equal("0180", Assert.Single(municipalities).Code);
    }

    [Fact]
    public async Task Only_codes_in_counties_01_to_25_are_municipalities()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var scb = new RecordingScbClient();
        scb.Tables[ScbCodeTables.MunicipalityTableName] =
        [
            new CodeItem("0114", "Upplands Väsby"),
            new CodeItem("2584", "Kiruna"),
            new CodeItem("0000", "Kommunkod saknas"),
            new CodeItem("9999", "Ej svensk hemortskommun"),
            new CodeItem("0099", "Under intervallet"),
            new CodeItem("2600", "Över intervallet"),
            new CodeItem("01AB", "Inte siffror"),
        ];
        var tables = new ScbCodeTables(scb, cache);

        var municipalities = await tables.GetMunicipalitiesAsync(CancellationToken.None);

        Assert.Equal(["2584", "0114"], municipalities.Select(row => row.Code));
    }

    [Fact]
    public async Task Classes_are_in_numeric_order_and_keep_their_codes()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var scb = new RecordingScbClient();
        scb.Tables[ScbCodeTables.EmployeeClassTableName] =
        [
            new CodeItem("10", "500-999"),
            new CodeItem("2", "1-4"),
            new CodeItem("0", "Uppgift saknas"),
        ];
        scb.Tables[ScbCodeTables.RevenueClassTableName] =
        [
            new CodeItem("12", "Stor"),
            new CodeItem("1", "Liten"),
        ];
        var tables = new ScbCodeTables(scb, cache);

        var employees = await tables.GetEmployeeClassesAsync(CancellationToken.None);
        var revenue = await tables.GetRevenueClassesAsync(CancellationToken.None);

        Assert.Equal(["0", "2", "10"], employees.Select(row => row.Code));
        Assert.Equal(["1", "12"], revenue.Select(row => row.Code));
    }

    [Fact]
    public async Task An_sni_code_is_found_with_its_name_and_an_unknown_one_is_null()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var scb = new RecordingScbClient();
        scb.Tables[ScbCodeTables.SniTableName] =
        [
            new CodeItem("96.210", "Frisörer"),
            new CodeItem("1110", "Odling"),
        ];
        var tables = new ScbCodeTables(scb, cache);

        Assert.Equal(
            new CodeResponse("96210", "Frisörer"),
            await tables.FindSniAsync("96210", CancellationToken.None));
        Assert.Equal(
            new CodeResponse("01110", "Odling"),
            await tables.FindSniAsync("01110", CancellationToken.None));
        Assert.Null(await tables.FindSniAsync("99999", CancellationToken.None));
    }

    [Fact]
    public async Task A_table_is_read_again_only_after_24_hours()
    {
        var clock = new ManualClock();
        using var cache = new MemoryCache(new MemoryCacheOptions { Clock = clock });
        var scb = new RecordingScbClient();
        scb.Tables[ScbCodeTables.EmployeeClassTableName] = [new CodeItem("2", "1-4")];
        var tables = new ScbCodeTables(scb, cache);

        await tables.GetEmployeeClassesAsync(CancellationToken.None);
        clock.UtcNow = clock.UtcNow.AddHours(23);
        await tables.GetEmployeeClassesAsync(CancellationToken.None);
        Assert.Equal(1, scb.TableCalls);

        clock.UtcNow = clock.UtcNow.AddHours(2);
        await tables.GetEmployeeClassesAsync(CancellationToken.None);
        Assert.Equal(2, scb.TableCalls);
    }

    [Fact]
    public async Task A_failed_call_is_not_cached()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var scb = new RecordingScbClient { Failure = new ScbException("HTTP 503.") };
        scb.Tables[ScbCodeTables.RevenueClassTableName] = [new CodeItem("1", "Liten")];
        var tables = new ScbCodeTables(scb, cache);

        await Assert.ThrowsAsync<ScbException>(() =>
            tables.GetRevenueClassesAsync(CancellationToken.None));

        scb.Failure = null;
        var revenue = await tables.GetRevenueClassesAsync(CancellationToken.None);

        Assert.Equal("1", Assert.Single(revenue).Code);
        Assert.Equal(2, scb.TableCalls);
    }

    private sealed class ManualClock : ISystemClock
    {
        public DateTimeOffset UtcNow { get; set; } =
            new(2026, 10, 30, 8, 0, 0, TimeSpan.Zero);
    }
}
