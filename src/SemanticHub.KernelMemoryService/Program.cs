using Microsoft.KernelMemory;
using Microsoft.KernelMemory.Service.AspNetCore;
using Scalar.AspNetCore;
using SemanticHub.ServiceDefaults;
using SemanticHub.KernelMemoryService.Extensions;

const string AzureBlobsConnectionStringKey = "blobs";
const string AzureOpenAIConnectionStringKey = "openai";
const string AzureOpenAITextConfigPath = "KernelMemory:Services:AzureOpenAIText";
const string AzureOpenAIEmbeddingConfigPath = "KernelMemory:Services:AzureOpenAIEmbedding";
const string AzureBlobsConfigPath = "KernelMemory:Services:AzureBlobs";
const string AzureQueuesConfigPath = "KernelMemory:Services:AzureQueues";
const string PostgresConfigPath = "KernelMemory:Services:Postgres";
const string PostgresConnectionStringKey = "postgres";

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi("v1", options => options
    .AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Info.Title = "Kernel Memory API";
        document.Info.Description = "API for managing memories and knowledge base using Microsoft Kernel Memory";
        document.Info.Version = "v1.0";
        return Task.CompletedTask;
    }));

var storageAccount = builder.Configuration.GetConnectionStringAccountName(AzureBlobsConnectionStringKey)
    ?? throw new InvalidOperationException("Azure Blobs connection string is not configured.");

var openaiEndpoint = builder.Configuration.GetConnectionStringEndpoint(AzureOpenAIConnectionStringKey)
    ?? throw new InvalidOperationException("Azure OpenAI connection string not configured.");

var textConfig = builder.Configuration.GetSection(AzureOpenAITextConfigPath).Get<AzureOpenAIConfig>()
    ?? throw new InvalidOperationException("Azure OpenAI text generation service is not configured.");

var embeddingConfig = builder.Configuration.GetSection(AzureOpenAIEmbeddingConfigPath).Get<AzureOpenAIConfig>()
    ?? throw new InvalidOperationException("Azure OpenAI embedding generation service is not configured.");

var storageConfig = builder.Configuration.GetSection(AzureBlobsConfigPath).Get<AzureBlobsConfig>()
    ?? throw new InvalidOperationException("Azure Blobs service is not configured.");

var queuesConfig = builder.Configuration.GetSection(AzureQueuesConfigPath).Get<AzureQueuesConfig>()
    ?? throw new InvalidOperationException("Azure Queues service is not configured.");

var postgresConfig = builder.Configuration.GetSection(PostgresConfigPath).Get<PostgresConfig>()
    ?? throw new InvalidOperationException("Postgres service is not configured.");

var postgresConnection = builder.Configuration.GetConnectionString(PostgresConnectionStringKey)
    ?? throw new InvalidOperationException("Postgres connection string is not configured.");

postgresConfig.ConnectionString = postgresConnection;
queuesConfig.Account = storageConfig.Account = storageAccount;
textConfig.Endpoint = embeddingConfig.Endpoint = openaiEndpoint;

var kernelMemoryBuilder = new KernelMemoryBuilder(builder.Services)
    .WithAzureQueuesOrchestration(queuesConfig)
    .WithAzureBlobsDocumentStorage(storageConfig)
    .WithAzureOpenAITextGeneration(textConfig)
    .WithAzureOpenAITextEmbeddingGeneration(embeddingConfig)
    .WithPostgresMemoryDb(postgresConfig);

var kernelMemory = kernelMemoryBuilder.Build<MemoryService>();
builder.Services.AddSingleton<IKernelMemory>(kernelMemory);
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi("/openapi/v1.json");
    app.MapScalarApiReference("/", options => options
        .WithTitle("Kernel Memory API")
        .WithOpenApiRoutePattern("/openapi/v1.json"));
}

app.UseHttpsRedirection();
app.AddKernelMemoryEndpoints("/api/memory");
app.AddKernelMemoryDiagnosticsEndpoints("/api/memory/diagnostics");
app.MapDefaultEndpoints();
app.Run();
