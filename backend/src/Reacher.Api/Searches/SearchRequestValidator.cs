using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Reacher.Api.Searches;

/// <summary>
/// Format checks for <c>POST /api/searches</c>. Whether a well-formed code exists is a
/// separate <see cref="ICodeLookup"/> call, so a bad shape never reaches SCB.
/// </summary>
public static partial class SearchRequestValidator
{
    public const int MaxMunicipalities = 20;

    public static bool TryValidate(
        SearchRequest? request,
        [NotNullWhen(true)] out ValidSearch? search,
        out List<SearchFieldError> errors)
    {
        errors = [];
        var sni = request?.Sni?.Trim();
        var municipalities = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(sni))
        {
            errors.Add(new("sni", "The sni field is required."));
        }
        else if (!SniPattern().IsMatch(sni))
        {
            errors.Add(new("sni", "The sni field must be a 5-digit code."));
        }

        foreach (var value in request?.Municipalities ?? [])
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var code = value.Trim();
            if (seen.Add(code))
            {
                municipalities.Add(code);
            }
        }

        if (municipalities.Count == 0)
        {
            errors.Add(new("municipalities", "At least one municipality is required."));
        }
        else if (municipalities.Count > MaxMunicipalities)
        {
            errors.Add(new(
                "municipalities",
                $"At most {MaxMunicipalities} municipalities are allowed."));
        }
        else if (municipalities.Exists(code => !MunicipalityPattern().IsMatch(code)))
        {
            errors.Add(new("municipalities", "Every municipality code must be 4 digits."));
        }

        if (errors.Count > 0)
        {
            search = null;
            return false;
        }

        search = new ValidSearch(sni!, municipalities, request?.Refresh ?? false);
        return true;
    }

    [GeneratedRegex("^[0-9]{5}$")]
    private static partial Regex SniPattern();

    [GeneratedRegex("^[0-9]{4}$")]
    private static partial Regex MunicipalityPattern();
}

public sealed record ValidSearch(
    string Sni,
    IReadOnlyList<string> Municipalities,
    bool Refresh);
