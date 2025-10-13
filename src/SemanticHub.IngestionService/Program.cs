using Azure;
using Azure.AI.OpenAI;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Scalar.AspNetCore;
using SemanticHub.IngestionService.Configuration;
using SemanticHub.IngestionService.Models;
using SemanticHub.IngestionService.Services;
using SemanticHub.IngestionService.Tools;
using SemanticHub.ServiceDefaults;
using OpenAI.Embeddings;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddOpenApi("v1", options =>
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "SemanticHub Ingestion Service";
        document.Info.Description = "Accepts documents and indexes them into Azure AI Search for Retrieval Augmented Generation.";
        document.Info.Version = "v1";
        return Task.CompletedTask;
    }));

var ingestionOptions = builder.Configuration.GetSection(IngestionOptions.SectionName).Get<IngestionOptions>() ?? new IngestionOptions();
ingestionOptions.ConfigureFromAspireServiceDiscovery(builder.Configuration);

builder.Services.AddSingleton(ingestionOptions);

builder.AddAzureOpenAIClient("openai");

builder.Services.AddSingleton<EmbeddingClient>(provider =>
{
    var options = provider.GetRequiredService<IngestionOptions>();
    if (string.IsNullOrWhiteSpace(options.AzureOpenAI.EmbeddingDeployment))
    {
        throw new InvalidOperationException("Ingestion:AzureOpenAI:EmbeddingDeployment must be configured.");
    }

    var openAiClient = provider.GetRequiredService<AzureOpenAIClient>();
    return openAiClient.GetEmbeddingClient(options.AzureOpenAI.EmbeddingDeployment);
});

builder.Services.AddSingleton(provider =>
{
    var options = provider.GetRequiredService<IngestionOptions>();
    if (string.IsNullOrEmpty(options.AzureSearch.Endpoint) || string.IsNullOrEmpty(options.AzureSearch.ApiKey))
    {
        throw new InvalidOperationException("Ingestion:AzureSearch settings are incomplete. Ensure endpoint and API key are configured.");
    }

    var endpoint = new Uri(options.AzureSearch.Endpoint);
    var credential = new AzureKeyCredential(options.AzureSearch.ApiKey);
    return new SearchClient(endpoint, options.AzureSearch.IndexName, credential);
});

builder.Services.AddSingleton(provider =>
{
    var options = provider.GetRequiredService<IngestionOptions>();
    var endpoint = new Uri(options.AzureSearch.Endpoint);
    var credential = new AzureKeyCredential(options.AzureSearch.ApiKey);
    return new SearchIndexClient(endpoint, credential);
});

builder.Services.AddSingleton<SearchIndexInitializer>();
builder.Services.AddSingleton<AzureOpenAIEmbeddingService>();
builder.Services.AddSingleton<AzureSearchIndexer>();
builder.Services.AddSingleton<MarkdownConverter>();
builder.Services.AddSingleton(provider =>
{
    var options = provider.GetRequiredService<IngestionOptions>();
    var logger = provider.GetRequiredService<ILogger<SemanticChunker>>();
    return new SemanticChunker(
        logger,
        options.Chunking.TargetTokenCount,
        options.Chunking.MaxTokenCount,
        options.Chunking.OverlapPercentage);
});


builder.Services.AddSingleton<WebScraperTool>();
builder.Services.AddSingleton<OpenApiIngestionTool>();

builder.Services.AddScoped<DocumentIngestionService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi("/openapi/v1.json");
    app.MapScalarApiReference("/", options => options
        .WithTitle("SemanticHub Ingestion Service")
        .WithOpenApiRoutePattern("/openapi/v1.json"));
}

app.UseHttpsRedirection();

app.MapPost("/ingestion/markdown", async (MarkdownIngestionRequest request, DocumentIngestionService ingestionService, CancellationToken cancellationToken) =>
{
    if (request is null || string.IsNullOrWhiteSpace(request.Content))
    {
        return Results.BadRequest(new { error = "Request content must not be empty." });
    }

    var result = await ingestionService.IngestMarkdownAsync(request, cancellationToken);

    var response = new IngestionResponse
    {
        Success = result.Success,
        DocumentId = result.DocumentId,
        IndexName = result.IndexName,
        ChunksIndexed = result.ChunksIndexed,
        Message = result.Message,
        ErrorMessage = result.Success ? null : result.Message
    };

    return Results.Ok(response);
})
.WithName("IngestMarkdown")
.WithSummary("Ingest Markdown content into Azure AI Search")
.WithDescription("Chunks, embeds, and indexes Markdown content so it can be retrieved by MAF agents.");

app.MapDefaultEndpoints();

app.Run();
