using Microsoft.Extensions.Configuration;

namespace SemanticHub.ServiceDefaults;

public static class ConfigurationExtensions
{
    public static string? GetConnectionStringEndpoint(this IConfiguration configuration, string connectionName, string? defaultValue = null) =>
        configuration.GetConnectionStringSetting(connectionName, "endpoint", defaultValue);

    public static string? GetConnectionStringAccountName(this IConfiguration configuration, string connectionName, string? defaultValue = null) =>
        configuration.GetConnectionStringSetting(connectionName, "accountname", defaultValue);

    public static string? GetConnectionStringSetting(this IConfiguration configuration, string connectionName, string key, string? defaultValue = null)
    {
        var connectionString = configuration.GetConnectionString(connectionName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return defaultValue;
        }

        foreach (var part in connectionString.Split(';'))
        {
            if (string.IsNullOrWhiteSpace(part)) continue;
            var kvp = part.Split('=', 2);
            if (kvp.Length != 2) continue;
            if (kvp[0].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return kvp[1].Trim();
            }
        }
        return defaultValue;
    }
}