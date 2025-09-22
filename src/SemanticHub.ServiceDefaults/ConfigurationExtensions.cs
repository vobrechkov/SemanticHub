using Microsoft.Extensions.Configuration;

namespace SemanticHub.ServiceDefaults;

public static class ConfigurationExtensions
{
    public static string? GetConnectionStringEndpoint(this IConfiguration configuration, string connectionName, string? defaultValue = null) =>
        configuration.GetConnectionStringSetting(connectionName, "endpoint", defaultValue);

    public static string? GetConnectionStringAccountName(this IConfiguration configuration, string connectionName, string? defaultValue = null) =>
        configuration.GetConnectionStringSetting(connectionName, "accountname", defaultValue);

    public static string? GetConnectionStringSetting(this IConfiguration configuration, string connectionName, string key, string? defaultValue = null) =>
        configuration.GetConnectionString(connectionName)?
            .Split(';')
            .FirstOrDefault(part => part.StartsWith($"{key}=", StringComparison.OrdinalIgnoreCase))?
            .Replace($"{key}=", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim() ?? defaultValue;
}