namespace Reacher.Api.Searches;

/// <summary>
/// Whether an SNI code and municipality codes are known. <c>POST /api/searches</c> uses
/// this; <see cref="ScbCodeLookup"/> answers from the tables behind <c>/api/codes</c>.
/// </summary>
public interface ICodeLookup
{
    Task<bool> IsKnownSniAsync(string sni, CancellationToken cancellationToken);

    /// <summary>Codes from <paramref name="municipalities"/> that are not municipalities.</summary>
    Task<IReadOnlyList<string>> UnknownMunicipalitiesAsync(
        IReadOnlyCollection<string> municipalities,
        CancellationToken cancellationToken);
}
