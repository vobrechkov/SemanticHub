using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SemanticHub.IngestionService.Configuration;
using SemanticHub.IngestionService.Services;
using SemanticHub.IngestionService.Tools;

namespace SemanticHub.IngestionService.Extensions;

/// <summary>
/// Extension methods for configuring the ingestion service.
/// </summary>
public static class IngestionServiceExtensions
{
    /// <summary>
    /// Adds Azure clients, options, and pipeline services required for ingestion.
    /// </summary>
    public static IHostApplicationBuilder AddIngestionServices(this IHostApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection(IngestionOptions.SectionName).Get<IngestionOptions>()
                      ?? new IngestionOptions();

        options.ConfigureFromServiceDiscovery(builder.Configuration);

        builder.Services.AddSingleton(options);

        var openAiClientBuilder = builder.AddAzureOpenAIClient("openai");
        if (string.IsNullOrWhiteSpace(options.AzureOpenAI.EmbeddingDeployment))
        {
            throw new InvalidOperationException("Ingestion:AzureOpenAI:EmbeddingDeployment must be configured.");
        }

        openAiClientBuilder.AddEmbeddingGenerator(options.AzureOpenAI.EmbeddingDeployment);

        builder.AddAzureBlobServiceClient("blobs");

        builder.AddAzureSearchClient("search");
        builder.Services.AddSingleton(provider =>
        {
            var ingestionOptions = provider.GetRequiredService<IngestionOptions>();
            var indexClient = provider.GetRequiredService<SearchIndexClient>();
            return indexClient.GetSearchClient(ingestionOptions.AzureSearch.IndexName);
        });

        builder.Services.AddIngestionPipeline();
        return builder;
    }

    /// <summary>
    /// Registers ingestion pipeline services with the dependency injection container.
    /// </summary>
    public static IServiceCollection AddIngestionPipeline(this IServiceCollection services)
    {
        services.AddSingleton<SearchIndexInitializer>();
        services.AddSingleton<AzureOpenAIEmbeddingService>();
        services.AddSingleton<AzureSearchIndexer>();
        services.AddSingleton<MarkdownConverter>();
        services.AddSingleton<BlobStorageService>();

        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IngestionOptions>();
            var logger = provider.GetRequiredService<ILogger<SemanticChunker>>();
            return new SemanticChunker(
                logger,
                options.Chunking.TargetTokenCount,
                options.Chunking.MaxTokenCount,
                options.Chunking.OverlapPercentage);
        });

        services.AddSingleton<WebScraperTool>();
        services.AddSingleton<OpenApiIngestionTool>();
        services.AddScoped<DocumentIngestionService>();

        return services;
    }
}
