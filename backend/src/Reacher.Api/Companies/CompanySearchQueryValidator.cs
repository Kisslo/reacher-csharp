using System.Globalization;
using System.Text.RegularExpressions;

namespace Reacher.Api.Companies;

/// <summary>Validates and converts the raw company search query.</summary>
public static partial class CompanySearchQueryValidator
{
    private static readonly string[] SortFields =
        ["name", "revenue", "netResult", "registeredAt", "employeeClass"];

    private static readonly string[] SortOrders = ["asc", "desc"];

    public static bool TryValidate(
        CompanySearchQuery query,
        out CompanySearchParameters? parameters,
        out IReadOnlyList<CompanySearchQueryError> errors)
    {
        var validationErrors = new List<CompanySearchQueryError>();
        var sni = query.Sni?.Trim();
        var municipalities = NormalizeValues(query.Municipality);
        var employeeClasses = NormalizeValues(query.EmployeeClass);
        var revenueClasses = NormalizeValues(query.RevenueClass);

        if (string.IsNullOrWhiteSpace(sni))
        {
            validationErrors.Add(new("sni", "The sni query parameter is required."));
        }
        else if (!SniPattern().IsMatch(sni))
        {
            validationErrors.Add(new("sni", "The sni query parameter must be a 5-digit code."));
        }

        if (municipalities.Count == 0)
        {
            validationErrors.Add(new(
                "municipality",
                "At least one municipality query parameter is required."));
        }

        var registeredFrom = ParseDate(
            query.RegisteredFrom,
            "registeredFrom",
            validationErrors);
        var registeredTo = ParseDate(query.RegisteredTo, "registeredTo", validationErrors);
        var revenueMin = ParseDecimal(query.RevenueMin, "revenueMin", validationErrors);
        var revenueMax = ParseDecimal(query.RevenueMax, "revenueMax", validationErrors);
        var netResultMin = ParseDecimal(query.NetResultMin, "netResultMin", validationErrors);
        var netResultMax = ParseDecimal(query.NetResultMax, "netResultMax", validationErrors);
        var hasFinancials = ParseBoolean(query.HasFinancials, "hasFinancials", validationErrors);
        var page = ParsePositiveInt(query.Page, "page", 1, validationErrors);
        var pageSize = ParsePositiveInt(query.PageSize, "pageSize", 50, validationErrors);

        var sort = NormalizeSingleValue(query.Sort, "name");
        if (!SortFields.Contains(sort, StringComparer.Ordinal))
        {
            validationErrors.Add(new(
                "sort",
                "The sort query parameter is not supported."));
        }

        var order = NormalizeSingleValue(query.Order, "asc");
        if (!SortOrders.Contains(order, StringComparer.Ordinal))
        {
            validationErrors.Add(new(
                "order",
                "The order query parameter must be 'asc' or 'desc'."));
        }

        if (pageSize > 200)
        {
            validationErrors.Add(new("pageSize", "The pageSize query parameter cannot exceed 200."));
        }

        if (registeredFrom is not null
            && registeredTo is not null
            && registeredFrom > registeredTo)
        {
            validationErrors.Add(new(
                "registeredFrom",
                "registeredFrom cannot be later than registeredTo."));
        }

        if (revenueMin is not null && revenueMax is not null && revenueMin > revenueMax)
        {
            validationErrors.Add(new("revenueMin", "revenueMin cannot exceed revenueMax."));
        }

        if (netResultMin is not null
            && netResultMax is not null
            && netResultMin > netResultMax)
        {
            validationErrors.Add(new("netResultMin", "netResultMin cannot exceed netResultMax."));
        }

        if (validationErrors.Count > 0)
        {
            parameters = null;
            errors = validationErrors;
            return false;
        }

        parameters = new CompanySearchParameters(
            sni!,
            municipalities,
            employeeClasses,
            revenueClasses,
            registeredFrom,
            registeredTo,
            revenueMin,
            revenueMax,
            netResultMin,
            netResultMax,
            hasFinancials,
            sort,
            order,
            page,
            pageSize);
        errors = [];
        return true;
    }

    private static List<string> NormalizeValues(IEnumerable<string> values) =>
        values
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static string NormalizeSingleValue(string? value, string defaultValue) =>
        string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();

    private static DateOnly? ParseDate(
        string? value,
        string parameterName,
        List<CompanySearchQueryError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateOnly.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed))
        {
            return parsed;
        }

        errors.Add(new(parameterName, $"The {parameterName} query parameter must be a date."));
        return null;
    }

    private static decimal? ParseDecimal(
        string? value,
        string parameterName,
        List<CompanySearchQueryError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (decimal.TryParse(
            value,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out var parsed))
        {
            return parsed;
        }

        errors.Add(new(parameterName, $"The {parameterName} query parameter must be a number."));
        return null;
    }

    private static bool? ParseBoolean(
        string? value,
        string parameterName,
        List<CompanySearchQueryError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (bool.TryParse(value, out var parsed))
        {
            return parsed;
        }

        errors.Add(new(parameterName, $"The {parameterName} query parameter must be true or false."));
        return null;
    }

    private static int ParsePositiveInt(
        string? value,
        string parameterName,
        int defaultValue,
        List<CompanySearchQueryError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            && parsed > 0)
        {
            return parsed;
        }

        errors.Add(new(parameterName, $"The {parameterName} query parameter must be a positive integer."));
        return defaultValue;
    }

    [GeneratedRegex("^[0-9]{5}$")]
    private static partial Regex SniPattern();
}

public sealed record CompanySearchQueryError(string Parameter, string Message);
