using Azure.Search.Documents.Indexes;
using SemanticHub.IngestionService.Configuration;
using SemanticHub.IngestionService.Application.Workflows;
using SemanticHub.IngestionService.Domain.Aggregates;
using SemanticHub.IngestionService.Domain.Ports;
using SemanticHub.IngestionService.Domain.Results;
using SemanticHub.IngestionService.Domain.Workflows;
using SemanticHub.IngestionService.Services;
using SemanticHub.IngestionService.Services.Processors;
using SemanticHub.IngestionService.Services.OpenApi;
using SemanticHub.IngestionService.Services.Scraping;
using SemanticHub.IngestionService.Services.Sitemaps;

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
        services.AddSingleton<IMarkdownConverter, MarkdownConverter>();
        services.AddSingleton<IBlobStorageService, BlobStorageService>();

        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IngestionOptions>();
            var logger = provider.GetRequiredService<ILogger<SemanticChunker>>();
            return new SemanticChunker(
                logger,
                options.Chunking.MinTokenCount,
                options.Chunking.TargetTokenCount,
                options.Chunking.MaxTokenCount,
                options.Chunking.OverlapPercentage);
        });

        services.AddSingleton<IHtmlScraper, PlaywrightHtmlScraper>();
        services.AddSingleton<ContentScorer>();
        services.AddScoped<IMarkdownProcessor, MarkdownProcessor>();
        services.AddScoped<IHtmlProcessor, HtmlProcessor>();
        services.AddHttpClient<IOpenApiSpecLocator, OpenApiSpecLocator>();
        services.AddSingleton<IOpenApiSpecParser, OpenApiSpecParser>();
        services.AddSingleton<IOpenApiMarkdownGenerator, OpenApiMarkdownGenerator>();
        services.AddSingleton<IOpenApiDocumentSplitter>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<OpenApiDocumentSplitter>>();
            var options = provider.GetRequiredService<IngestionOptions>().OpenApi;
            return new OpenApiDocumentSplitter(logger, options);
        });

        services.AddHttpClient<ISitemapFetcher, HttpSitemapFetcher>()
            .ConfigureHttpClient((provider, client) =>
            {
                var sitemapOptions = provider.GetRequiredService<IngestionOptions>().Sitemap;
                client.Timeout = TimeSpan.FromSeconds(sitemapOptions.FetchTimeoutSeconds);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(sitemapOptions.UserAgent);
            });

        services.AddHttpClient<IUrlFilterPolicy, SitemapUrlFilterPolicy>()
            .ConfigureHttpClient((provider, client) =>
            {
                var sitemapOptions = provider.GetRequiredService<IngestionOptions>().Sitemap;
                client.Timeout = TimeSpan.FromSeconds(sitemapOptions.FetchTimeoutSeconds);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(sitemapOptions.UserAgent);
            });

        services.AddSingleton<ISitemapParser, XmlSitemapParser>();
        services.AddSingleton<IChangeFrequencyHeuristic, DefaultChangeFrequencyHeuristic>();

        services.AddScoped<IIngestionWorkflow<SitemapIngestion, SitemapIngestionResult>, SiteMapIngestionWorkflow>();

        services.AddScoped<IIngestionWorkflow<MarkdownDocumentIngestion>, MarkdownIngestionWorkflow>();
        services.AddScoped<IIngestionWorkflow<HtmlDocumentIngestion>, HtmlIngestionWorkflow>();
        services.AddScoped<IIngestionWorkflow<WebPageIngestion>, WebPageIngestionWorkflow>();
        services.AddScoped<IIngestionWorkflow<BatchWebPageIngestion, BatchWebPageIngestionResult>, BatchWebPageIngestionWorkflow>();
        services.AddScoped<IIngestionWorkflow<BulkMarkdownIngestion, BlobIngestionResult>, BlobIngestionWorkflow>();
        services.AddScoped<IIngestionWorkflow<OpenApiSpecificationIngestion, OpenApiIngestionResult>, OpenApiIngestionWorkflow>();

        return services;
    }
}
