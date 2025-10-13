using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using SemanticHub.Api.Configuration;
using SemanticHub.Api.Memory;
using SemanticHub.Api.Services;
using SemanticHub.Api.Tools;

namespace SemanticHub.Api.Extensions;

/// <summary>
/// Extension methods for registering Agent Framework services
/// </summary>
public static class AgentFrameworkServiceExtensions
{
    /// <summary>
    /// Adds Microsoft Agent Framework services to the DI container
    /// </summary>
    public static IServiceCollection AddAgentFramework(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration.GetSection(AgentFrameworkOptions.SectionName)
            .Get<AgentFrameworkOptions>()
            ?? throw new InvalidOperationException("AgentFramework configuration section is missing");

        options.ConfigureFromServiceDiscovery(configuration);

        services.AddSingleton(options);

        services.AddSingleton<ChatClientFactory>();
        services.AddSingleton<IChatClient>(sp =>
        {
            var factory = sp.GetRequiredService<ChatClientFactory>();
            return factory.CreateChatClient();
        });

        // Azure AI Search retrieval plumbing
        if (!string.IsNullOrEmpty(options.Memory.AzureSearch.IndexName))
        {
            services.AddSingleton(sp =>
            {
                var indexClient = sp.GetRequiredService<SearchIndexClient>();
                return indexClient.GetSearchClient(options.Memory.AzureSearch.IndexName);
            });

            services.AddSingleton<IAzureSearchKnowledgeStore, AzureSearchKnowledgeStore>();
            services.AddSingleton<AIContextProvider, AzureSearchContextProvider>();
        }

        services.AddSingleton<KnowledgeBaseTools>();
        services.AddSingleton<AgentService>();

        return services;
    }

    /// <summary>
    /// Configures the Agent Framework options from Aspire service discovery
    /// </summary>
    public static void ConfigureFromServiceDiscovery(
        this AgentFrameworkOptions options,
        IConfiguration configuration)
    {
        var openAiEndpoint = configuration.GetConnectionStringEndpoint("openai");
        if (!string.IsNullOrEmpty(openAiEndpoint))
        {
            options.AzureOpenAI.Endpoint = openAiEndpoint;
        }

        var openAiKey = configuration.GetConnectionStringValue("openai", "Key");
        if (!string.IsNullOrEmpty(openAiKey))
        {
            options.AzureOpenAI.ApiKey = openAiKey;
        }

        var searchEndpoint = configuration.GetConnectionStringEndpoint("search");
        if (!string.IsNullOrEmpty(searchEndpoint))
        {
            options.Memory.AzureSearch.Endpoint = searchEndpoint;
        }

        var searchKey = configuration.GetConnectionStringValue("search", "Key");
        if (!string.IsNullOrEmpty(searchKey))
        {
            options.Memory.AzureSearch.ApiKey = searchKey;
        }
    }
}

/// <summary>
/// Configuration extensions for Aspire integration
/// </summary>
public static class ConfigurationExtensions
{
    public static string? GetConnectionStringEndpoint(this IConfiguration configuration, string name)
        => configuration.GetConnectionStringValue(name, "Endpoint");

    public static string? GetConnectionStringValue(this IConfiguration configuration, string name, string key)
    {
        var connectionString = configuration.GetConnectionString(name);
        if (string.IsNullOrEmpty(connectionString))
        {
            return null;
        }

        var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);
        var match = parts.FirstOrDefault(p => p.StartsWith($"{key}=", StringComparison.OrdinalIgnoreCase));

        return match?[(key.Length + 1)..];
    }
}
