namespace Reacher.Api.Searches;

/// <summary>
/// Whether an SNI code and municipality codes are known. T1-08 will own the code-table
/// endpoints; <see cref="ScbCodeLookup"/> is the check <c>POST /api/searches</c> uses until
/// that cache can be shared.
/// </summary>
public interface ICodeLookup
{
    Task<bool> IsKnownSniAsync(string sni, CancellationToken cancellationToken);

    /// <summary>Codes from <paramref name="municipalities"/> that are not municipalities.</summary>
    Task<IReadOnlyList<string>> UnknownMunicipalitiesAsync(
        IReadOnlyCollection<string> municipalities,
        CancellationToken cancellationToken);
}
