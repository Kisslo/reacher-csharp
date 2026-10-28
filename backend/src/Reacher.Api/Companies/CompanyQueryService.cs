using System.Data;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using Reacher.Data;
using Reacher.Ingestion.Options;

namespace Reacher.Api.Companies;

public sealed class CompanyQueryService(
    ReacherDbContext db,
    IOptions<IngestOptions> ingestOptions,
    TimeProvider time) : ICompanyQueryService
{
    private const string SqlResourceName = "Reacher.Api.Companies.CompanySearch.sql";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CompanySearchResponse> SearchAsync(
        CompanySearchParameters parameters,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = LoadSql();
        command.CommandType = CommandType.Text;
        AddParameters(command, parameters);

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("The company search query returned no response.");
        }

        var json = reader.GetFieldValue<string>(0);
        return JsonSerializer.Deserialize<CompanySearchResponse>(json, JsonOptions)
            ?? throw new InvalidOperationException("The company search query returned invalid JSON.");
    }

    private void AddParameters(
        IDbCommand command,
        CompanySearchParameters parameters)
    {
        command.Parameters.Add(CreateTextArrayParameter("sni_municipalities", parameters.Municipalities));
        command.Parameters.Add(CreateTextArrayParameter("employee_classes", parameters.EmployeeClasses));
        command.Parameters.Add(CreateTextArrayParameter("revenue_classes", parameters.RevenueClasses));
        command.Parameters.Add(new NpgsqlParameter<DateOnly?>("registered_from", parameters.RegisteredFrom));
        command.Parameters.Add(new NpgsqlParameter<DateOnly?>("registered_to", parameters.RegisteredTo));
        command.Parameters.Add(new NpgsqlParameter<decimal?>("revenue_min", parameters.RevenueMin));
        command.Parameters.Add(new NpgsqlParameter<decimal?>("revenue_max", parameters.RevenueMax));
        command.Parameters.Add(new NpgsqlParameter<decimal?>("net_result_min", parameters.NetResultMin));
        command.Parameters.Add(new NpgsqlParameter<decimal?>("net_result_max", parameters.NetResultMax));
        command.Parameters.Add(new NpgsqlParameter<bool>("has_financials", parameters.HasFinancials == true));
        command.Parameters.Add(new NpgsqlParameter<string>("sni", parameters.Sni));
        command.Parameters.Add(new NpgsqlParameter<string>("sort", parameters.Sort));
        command.Parameters.Add(new NpgsqlParameter<string>("order", parameters.Order));
        command.Parameters.Add(new NpgsqlParameter<int>("page_size", parameters.PageSize));
        command.Parameters.Add(new NpgsqlParameter<int>(
            "page_offset",
            (parameters.Page - 1) * parameters.PageSize));
        command.Parameters.Add(new NpgsqlParameter<DateTimeOffset>("stale_cutoff", time.GetUtcNow()
            .AddDays(-ingestOptions.Value.FreshnessDays)));
    }

    private static NpgsqlParameter<string[]> CreateTextArrayParameter(
        string name,
        IReadOnlyList<string> values) =>
        new(name, values.ToArray())
        {
            NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text
        };

    private static string LoadSql()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(SqlResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded SQL resource '{SqlResourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
