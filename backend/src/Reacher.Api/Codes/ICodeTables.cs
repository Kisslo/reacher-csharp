namespace Reacher.Api.Codes;

/// <summary>
/// SCB's code lists, for the <c>/api/codes</c> endpoints and the code checks in
/// <c>POST /api/searches</c>.
/// </summary>
public interface ICodeTables
{
    /// <summary>Every municipality, sorted by name in Swedish order (Å, Ä and Ö last).</summary>
    Task<IReadOnlyList<CodeResponse>> GetMunicipalitiesAsync(CancellationToken cancellationToken);

    /// <summary>Employee classes in code order, which is also size order.</summary>
    Task<IReadOnlyList<CodeResponse>> GetEmployeeClassesAsync(CancellationToken cancellationToken);

    /// <summary>Revenue classes in code order, which is also size order.</summary>
    Task<IReadOnlyList<CodeResponse>> GetRevenueClassesAsync(CancellationToken cancellationToken);

    /// <summary>One SNI code, or null if SCB does not have it.</summary>
    Task<CodeResponse?> FindSniAsync(string code, CancellationToken cancellationToken);
}
