namespace SemanticHub.IngestionService.Configuration;

public static class ConfigurationExtensions
{
    public static void ConfigureFromServiceDiscovery(this IngestionOptions options, IConfiguration configuration)
    {
        var openAiEndpoint = configuration.GetConnectionStringSetting("openai", "Endpoint");
        if (!string.IsNullOrEmpty(openAiEndpoint))
        {
            options.AzureOpenAI.Endpoint = openAiEndpoint;
        }

        var openAiKey = configuration.GetConnectionStringSetting("openai", "Key");
        if (!string.IsNullOrEmpty(openAiKey))
        {
            options.AzureOpenAI.ApiKey = openAiKey;
        }

        var searchEndpoint = configuration.GetConnectionStringSetting("search", "Endpoint");
        if (!string.IsNullOrEmpty(searchEndpoint))
        {
            options.AzureSearch.Endpoint = searchEndpoint;
        }

        var searchKey = configuration.GetConnectionStringSetting("search", "Key");
        if (!string.IsNullOrEmpty(searchKey))
        {
            options.AzureSearch.ApiKey = searchKey;
        }
    }

    private static string? GetConnectionStringSetting(this IConfiguration configuration, string connectionName, string key)
    {
        var connectionString = configuration.GetConnectionString(connectionName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kvp = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (kvp.Length != 2)
            {
                continue;
            }

            if (kvp[0].Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return kvp[1];
            }
        }

        return null;
    }
}
