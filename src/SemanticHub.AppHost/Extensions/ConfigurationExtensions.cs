using Microsoft.Extensions.Configuration;

namespace SemanticHub.AppHost.Extensions;

public static class ConfigurationExtensions
{
    public static string? GetConnectionStringEndpoint(this IConfiguration configuration, string connectionName) =>
        configuration.GetConnectionStringSetting(connectionName, "endpoint");

    public static string? GetConnectionStringAccountName(this IConfiguration configuration, string connectionName) =>
        configuration.GetConnectionStringSetting(connectionName, "accountname");

    public static string? GetConnectionStringSetting(this IConfiguration configuration, string connectionName, string key) =>
        configuration.GetConnectionString(connectionName)?
            .Split(';')
            .FirstOrDefault(part => part.StartsWith($"{key}=", StringComparison.OrdinalIgnoreCase))?
            .Replace($"{key}=", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();
}