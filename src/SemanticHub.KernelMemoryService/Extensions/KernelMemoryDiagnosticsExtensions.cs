using Microsoft.KernelMemory;
using Npgsql;

namespace SemanticHub.KernelMemoryService.Extensions;

public static class KernelMemoryDiagnosticsExtensions
{
    const string PostgresConnectionStringKey = "postgres";
    
    public static WebApplication AddKernelMemoryDiagnosticsEndpoints(this WebApplication app, string pathPrefix = "diagnostics")
    {
        var group = app.MapGroup(pathPrefix)
            .WithDescription("Kernel Memory diagnostics endpoints")
            .WithTags("diagnostics");
        
        group.MapGet("tables", HandleGetTablesAsync);
        group.MapGet("memory-count", HandleGetMemoryCountAsync);
        group.MapGet("memory-sample", HandleGetMemorySampleAsync);
        group.MapPost("test-search", HandleTestSearchAsync);

        return app;
    }

    private static async Task<IResult> HandleGetTablesAsync(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(PostgresConnectionStringKey)
                               ?? throw new InvalidOperationException("Postgres connection string is not configured.");
        var tablePrefix = configuration["KernelMemory:Services:Postgres:TablePrefix"] ?? "km_";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        const string query =
            """
            SELECT table_name, column_name, data_type
            FROM information_schema.columns
            WHERE table_schema = 'public'
            AND (table_name LIKE @tablePattern)
            ORDER BY table_name, ordinal_position
            """;

        await using var command = new NpgsqlCommand(query, connection);
        command.Parameters.AddWithValue("@tablePattern", $"{tablePrefix}%");
        await using var reader = await command.ExecuteReaderAsync();

        var tables = new List<object>();
        while (await reader.ReadAsync())
        {
            tables.Add(new
            {
                TableName = reader.GetString(0),
                ColumnName = reader.GetString(1),
                DataType = reader.GetString(2)
            });
        }

        return Results.Ok(tables);
    }

    private static async Task<IResult> HandleGetMemoryCountAsync(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(PostgresConnectionStringKey)
                               ?? throw new InvalidOperationException(
                                   "Postgres connection string is not configured.");
        var tablePrefix = configuration["KernelMemory:Services:Postgres:TablePrefix"] ?? "km_";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        var results = new Dictionary<string, object>();

        // First, discover actual Kernel Memory tables
        const string discoverQuery =
            """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'public'
            AND table_name LIKE @tablePattern
            """;

        await using var discoverCommand = new NpgsqlCommand(discoverQuery, connection);
        discoverCommand.Parameters.AddWithValue("@tablePattern", $"{tablePrefix}%");
        await using var discoverReader = await discoverCommand.ExecuteReaderAsync();

        var tableNames = new List<string>();
        while (await discoverReader.ReadAsync())
        {
            tableNames.Add(discoverReader.GetString(0));
        }

        await discoverReader.CloseAsync();

        results["tables"] = tableNames;

        // Get count for each discovered table
        foreach (var tableName in tableNames)
        {
            try
            {
                var query = $"SELECT COUNT(*) FROM \"{tableName}\"";
                await using var command = new NpgsqlCommand(query, connection);
                var count = await command.ExecuteScalarAsync();
                results[tableName] = count ?? 0;
            }
            catch (Exception ex)
            {
                results[tableName] = $"Error: {ex.Message}";
            }
        }

        if (tableNames.Count == 0)
        {
            results["message"] =
                $"No Kernel Memory tables found with prefix '{tablePrefix}'. Check if documents have been uploaded and processed.";
        }

        return Results.Ok(results);
    }
    
    private static async Task<IResult> HandleGetMemorySampleAsync(IConfiguration configuration, int limit = 5)
    {
        var connectionString = configuration.GetConnectionString(PostgresConnectionStringKey)
                               ?? throw new InvalidOperationException(
                                   "Postgres connection string is not configured.");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        var tablePrefix = configuration["KernelMemory:Services:Postgres:TablePrefix"] ?? "km_";

        // Find actual Kernel Memory tables
        const string findTablesQuery =
            """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'public'
            AND table_name LIKE @tablePattern
            """;

        await using var findCommand = new NpgsqlCommand(findTablesQuery, connection);
        findCommand.Parameters.AddWithValue("@tablePattern", $"{tablePrefix}%");
        await using var findReader = await findCommand.ExecuteReaderAsync();

        var tableNames = new List<string>();
        while (await findReader.ReadAsync())
        {
            tableNames.Add(findReader.GetString(0));
        }

        await findReader.CloseAsync();

        var results = new Dictionary<string, object>
        {
            ["tables"] = tableNames,
            ["prefix"] = tablePrefix
        };

        // Try to get sample data from the first table
        if (tableNames.Count > 0)
        {
            var tableName = tableNames[0];
            try
            {
                var query = $"SELECT * FROM \"{tableName}\" LIMIT {limit}";
                await using var command = new NpgsqlCommand(query, connection);
                await using var reader = await command.ExecuteReaderAsync();

                var samples = new List<Dictionary<string, object>>();
                while (await reader.ReadAsync())
                {
                    var row = new Dictionary<string, object>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        var columnName = reader.GetName(i);
                        var dataTypeName = reader.GetDataTypeName(i);

                        // Handle vector columns (pgvector extension)
                        if (dataTypeName == "public.vector")
                        {
                            row[columnName] = "[Vector data - embedding]";
                        }
                        else
                        {
                            var value = reader.GetValue(i);
                            // Convert byte arrays to length info for readability
                            if (value is byte[] bytes)
                            {
                                row[columnName] = $"[Binary data: {bytes.Length} bytes]";
                            }
                            else
                            {
#pragma warning disable CS8601 // Possible null reference assignment
                                row[columnName] = value is DBNull ? null : value;
#pragma warning restore CS8601 // Possible null reference assignment
                            }
                        }
                    }

                    samples.Add(row);
                }

                results["samples"] = samples;
                results["table"] = tableName;
            }
            catch (Exception ex)
            {
                results["error"] = ex.Message;
            }
        }
        else
        {
            results["message"] =
                $"No Kernel Memory tables found with prefix '{tablePrefix}'. Check if documents have been uploaded and processed.";
        }

        return Results.Ok(results);
    }

    private static async Task<IResult> HandleTestSearchAsync(TestSearchRequest request, IConfiguration configuration,
        IKernelMemory memory)
    {
        try
        {
            var searchResult = await memory.SearchAsync(
                query: request.Query,
                index: request.Index,
                filter: request.Filter?.FirstOrDefault(),
                minRelevance: request.MinRelevance,
                limit: request.Limit);

            return Results.Ok(new
            {
                ResultCount = searchResult.Results.Count,
                Results = searchResult.Results
            });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Search error: {ex.Message}");
        }
    }
}

public record TestSearchRequest(
    string Query,
    string? Index = null,
    MemoryFilter[]? Filter = null,
    double MinRelevance = 0.0,
    int Limit = 10);