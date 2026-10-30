using Microsoft.Extensions.Caching.Memory;
using Reacher.Api.Codes;
using Reacher.Api.Searches;
using Reacher.Ingestion.Scb;

namespace Reacher.Api.Tests;

public sealed class ScbCodeLookupTests
{
    [Fact]
    public async Task Known_codes_match_after_normalising_and_the_table_is_cached()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var scb = new RecordingScbClient();
        scb.Tables[ScbCodeTables.SniTableName] =
        [
            new CodeItem("96.210", "Example"),
            new CodeItem("96220", "Example"),
        ];
        scb.Tables[ScbCodeTables.MunicipalityTableName] =
        [
            new CodeItem("180", "Example"),
            new CodeItem("0184", "Example"),
        ];
        var lookup = new ScbCodeLookup(new ScbCodeTables(scb, cache));

        Assert.True(await lookup.IsKnownSniAsync("96210", CancellationToken.None));
        Assert.False(await lookup.IsKnownSniAsync("99999", CancellationToken.None));
        Assert.Equal(1, scb.TableCalls);

        var unknown = await lookup.UnknownMunicipalitiesAsync(
            ["0180", "9999"],
            CancellationToken.None);
        Assert.Equal(["9999"], unknown);
        Assert.Equal(2, scb.TableCalls);

        Assert.True(await lookup.IsKnownSniAsync("96220", CancellationToken.None));
        await lookup.UnknownMunicipalitiesAsync(["0184"], CancellationToken.None);
        Assert.Equal(2, scb.TableCalls);
    }

    [Fact]
    public async Task Scb_placeholder_rows_are_not_known_municipalities()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var scb = new RecordingScbClient();
        scb.Tables[ScbCodeTables.MunicipalityTableName] =
        [
            new CodeItem("0180", "Stockholm"),
            new CodeItem("0000", "Kommunkod saknas"),
            new CodeItem("9999", "Ej svensk hemortskommun"),
        ];
        var lookup = new ScbCodeLookup(new ScbCodeTables(scb, cache));

        var unknown = await lookup.UnknownMunicipalitiesAsync(
            ["0180", "0000", "9999"],
            CancellationToken.None);

        Assert.Equal(["0000", "9999"], unknown);
    }

    [Fact]
    public async Task Empty_table_is_not_cached()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var scb = new RecordingScbClient();
        scb.Tables[ScbCodeTables.SniTableName] = [];
        var lookup = new ScbCodeLookup(new ScbCodeTables(scb, cache));

        await Assert.ThrowsAsync<ScbException>(() =>
            lookup.IsKnownSniAsync("96210", CancellationToken.None));
        await Assert.ThrowsAsync<ScbException>(() =>
            lookup.IsKnownSniAsync("96210", CancellationToken.None));

        Assert.Equal(2, scb.TableCalls);
    }
}
