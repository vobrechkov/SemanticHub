using Scalar.AspNetCore;
using SemanticHub.Api.Configuration;
using SemanticHub.Api.Endpoints;
using SemanticHub.Api.Extensions;
using SemanticHub.Api.Services;
using SemanticHub.Api.Tools;
using SemanticHub.Api.Workflows;
using SemanticHub.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

// Add Aspire service defaults (health checks, service discovery, telemetry)
builder.AddServiceDefaults();
var openAiClientBuilder = builder.AddAzureOpenAIClient("openai");

// Add OpenAPI/Swagger
builder.Services.AddOpenApi("v1", options => options
    .AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Info.Title = "SemanticHub Agent API";
        document.Info.Description = "API for AI agents powered by Microsoft Agent Framework with knowledge base integration and multi-agent workflows";
        document.Info.Version = "v1.0";
        return Task.CompletedTask;
    }));

// Configure Agent Framework options from Aspire service discovery
var agentOptions = builder.Configuration.GetSection(AgentFrameworkOptions.SectionName)
    .Get<AgentFrameworkOptions>()
    ?? new AgentFrameworkOptions();

agentOptions.ConfigureFromServiceDiscovery(builder.Configuration);
if (agentOptions.Memory.Provider == MemoryProvider.AzureSearch)
{
    builder.AddAzureSearchClient("search");
}

if (string.IsNullOrWhiteSpace(agentOptions.AzureOpenAI.EmbeddingDeployment))
{
    throw new InvalidOperationException("AgentFramework:AzureOpenAI:EmbeddingDeployment must be configured.");
}

openAiClientBuilder.AddEmbeddingGenerator(agentOptions.AzureOpenAI.EmbeddingDeployment);

builder.Services.AddHttpClient<IngestionClient>("ingestion", client =>
{
    client.BaseAddress = new Uri("https+http://ingestion");
});


// Add Microsoft Agent Framework services
builder.Services.AddAgentFramework(builder.Configuration);

// Register tools
builder.Services.AddSingleton<IngestionTools>();

// Register workflows
builder.Services.AddScoped<KnowledgeIngestionWorkflow>();
builder.Services.AddScoped<ResearchWorkflow>();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi("/openapi/v1.json");
    app.MapScalarApiReference("/", options => options
        .WithTitle("SemanticHub Agent API")
        .WithOpenApiRoutePattern("/openapi/v1.json"));
}

app.UseHttpsRedirection();

// Map agent endpoints
app.MapAgentEndpoints();

// Map workflow endpoints
app.MapWorkflowEndpoints();

// Map default health check endpoints from Aspire
app.MapDefaultEndpoints();

app.Run();
