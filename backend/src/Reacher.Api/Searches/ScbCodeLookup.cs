using Reacher.Api.Codes;

namespace Reacher.Api.Searches;

/// <summary>
/// Checks codes against SCB's code tables. Uses the same cached tables as
/// <c>/api/codes</c>, so a code the dropdown offers is a code the search accepts.
/// </summary>
public sealed class ScbCodeLookup(ICodeTables tables) : ICodeLookup
{
    public async Task<bool> IsKnownSniAsync(string sni, CancellationToken cancellationToken) =>
        await tables.FindSniAsync(sni, cancellationToken) is not null;

    public async Task<IReadOnlyList<string>> UnknownMunicipalitiesAsync(
        IReadOnlyCollection<string> municipalities,
        CancellationToken cancellationToken)
    {
        var known = await tables.GetMunicipalitiesAsync(cancellationToken);
        var codes = known.Select(static row => row.Code).ToHashSet(StringComparer.Ordinal);
        return municipalities.Where(code => !codes.Contains(code)).ToList();
    }
}
