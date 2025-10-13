# SemanticHub Copilot Instructions

This is a .NET 9 solution demonstrating Microsoft Agent Framework (MAF) RAG with Azure AI Search, Azure OpenAI, and .NET Aspire orchestration.

## Architecture Overview

### Service Orchestration via Aspire
All services depend on `.AddServiceDefaults()` from `SemanticHub.ServiceDefaults` which configures:
- Service discovery (DNS-based with `https+http://` scheme preference)
- OpenTelemetry (traces, metrics, logs)
- Standard resilience handlers for HTTP clients
- Health checks at `/health` (all checks) and `/alive` (liveness only)

**Critical Dependency Chain** (`AppHost.cs`):
1. Azure OpenAI → provisions chat (`gpt-4o-mini`) and embedding (`text-embedding-3-small`) deployments
2. Azure AI Search → creates index with hybrid vector+keyword search
3. Azure Blob Storage → uses Azurite emulator locally, real storage in Azure
4. IngestionService → depends on OpenAI + Search
5. Agent API → depends on OpenAI + Search + Blob + IngestionService
6. WebApp (Next.js) → depends on Agent API

**Key Pattern**: Services use `WaitFor()` to ensure dependencies are healthy before starting. Resource names in `AppHost.cs` must be globally unique (e.g., `semhub-eus-dev-search`).

### Configuration Strategy
All services follow this pattern in `appsettings.json`:
- Base configuration with default values
- `ConfigureFromServiceDiscovery()` extension method overrides from Aspire connection strings
- Connection strings parsed for `Endpoint=` and `Key=` values
- Deployment names injected via environment variables (e.g., `AgentFramework__AzureOpenAI__ChatDeployment`)

Example from `AgentFrameworkServiceExtensions.cs`:
```csharp
var openAiEndpoint = configuration.GetConnectionStringEndpoint("openai");
options.AzureOpenAI.Endpoint = openAiEndpoint; // Overrides appsettings
```

### Memory Provider Abstraction
`IKnowledgeStore` interface supports multiple backends:
- **AzureSearchKnowledgeStore** (default) - hybrid vector+keyword search with semantic ranker
- **OpenSearchKnowledgeStore** - local development without Azure dependencies

Switch via `AgentFramework__Memory__Provider` environment variable or `appsettings.json`.

## Development Workflows

### Initial Setup (CRITICAL)
Azure RBAC roles must be assigned before local development works:
```bash
cd src/SemanticHub.AppHost/Scripts
./setup-all-rbac.sh  # Assigns Storage Blob Data Contributor, Search Index/Service Contributor, Cognitive Services OpenAI User
# Wait 5-10 minutes for Azure RBAC propagation
```
Without this, you'll get "403 Forbidden" even with valid `DefaultAzureCredential`.

### Build and Run
```bash
dotnet build src/SemanticHub.sln
dotnet run --project src/SemanticHub.AppHost  # Starts all services
```
Aspire Dashboard URL shown in terminal output (typically `https://localhost:17xxx`).

### Testing
```bash
dotnet test src/SemanticHub.Tests
dotnet test --filter "FullyQualifiedName~AgentServiceTests.CreateAgent"
```

### Adding New Agent Tools
1. Create method in `Tools/` directory with `[Description]` attribute
2. Register in `AgentFrameworkServiceExtensions.AddAgentFramework()`
3. Pass to agent via `AIFunctionFactory.Create(yourMethod)`

Example from `KnowledgeBaseTools.cs`:
```csharp
[Description("Search the knowledge base for information related to a query")]
public async Task<string> SearchKnowledgeBase(
    [Description("The search query")] string query,
    double minRelevance = 0.0,
    int limit = 0,
    CancellationToken cancellationToken = default)
```

### Creating Multi-Agent Workflows
Use `AgentWorkflowBuilder` to chain agents sequentially:
```csharp
var workflow = AgentWorkflowBuilder
    .BuildSequential(validatorAgent, extractorAgent, indexerAgent, verifierAgent);
return await workflow.AsAgentAsync();
```
See `KnowledgeIngestionWorkflow.cs` for full example with 4-agent orchestration.

## Project-Specific Patterns

### Document Ingestion Pipeline
1. **SemanticChunker** splits documents respecting:
   - Markdown boundaries (h1/h2/h3, code blocks, lists)
   - Token limits (`TargetTokens: 500`, `MaxTokens: 1000`, `OverlapTokens: 100`)
   - Maintains heading hierarchy in each chunk
2. **AzureOpenAIEmbeddingService** generates embeddings via `text-embedding-3-small`
3. **AzureSearchIndexer** upserts chunks with:
   - Vector field for semantic search
   - Full-text fields for keyword search
   - Metadata (title, summary, tags, sourceUrl, sourceType)
   - Parent document tracking via `parentDocumentId`

### Minimal API Endpoints
Endpoints use `MapPost`/`MapGet` with inline handlers:
```csharp
app.MapPost("/api/agents/chat", async (AgentRequest request, AgentService agentService) => 
{
    var agent = agentService.CreateDefaultAgent(tools);
    var response = await agent.InvokeAsync(request.Message);
    return Results.Ok(response);
})
.WithName("AgentChat")
.WithSummary("Single-turn chat with MAF agent");
```
Use `.WithName()` for OpenAPI operation IDs, `.WithSummary()` for descriptions.

### Agent Framework Conventions
- **IChatClient**: Created via factory pattern from `AzureOpenAIClient`
- **AIAgent**: Created with `chatClient.CreateAIAgent(instructions, name, tools)`
- **AIContextProvider**: Custom interface for injecting grounding context (see `KnowledgeStoreContextProvider`)
- **AITool**: Registered functions wrapped via `AIFunctionFactory.Create()`
- **Streaming**: Not yet implemented for workflows (MAF limitation)

### Azure AI Search Field Mapping
Index schema in `SearchIndexInitializer.cs` defines:
- `id` (string, key) - chunk identifier
- `content` (string, searchable) - text content
- `contentVector` (Collection(Edm.Single), searchable) - embedding
- `parentDocumentId` (string, facetable) - groups chunks
- `title`, `summary` (string, searchable) - metadata
- `tags` (Collection(Edm.String), facetable, filterable) - categorization

Field names configurable via `IngestionOptions.AzureSearch.*Field` properties.

### OpenAPI Specification Ingestion
Special tool for API documentation (`OpenApiIngestionTool.cs`):
- Parses OpenAPI 3.x YAML/JSON specs
- Creates one Markdown document per endpoint
- Extracts parameters, schemas, examples
- Adds YAML frontmatter with structured metadata
- Enables semantic search over API endpoints

## Key Files to Understand

- **AppHost.cs** - Aspire orchestration, resource provisioning, dependency graph
- **AgentFrameworkServiceExtensions.cs** - DI registration, service discovery integration
- **KnowledgeBaseTools.cs** - Core agent tools for search and document management
- **AzureSearchKnowledgeStore.cs** - Hybrid search implementation with semantic ranker
- **SemanticChunker.cs** - Markdown-aware chunking algorithm
- **KnowledgeIngestionWorkflow.cs** - Multi-agent workflow orchestration pattern

## Common Pitfalls

### RBAC Timing
Role assignments take 5-10 minutes to propagate. Always wait after running setup scripts. Re-login (`az logout && az login`) can force refresh.

### Service Discovery Connection Strings
Aspire injects connection strings like `Endpoint=https://...;Key=...`. Parse with `GetConnectionStringValue()` extension (see `ConfigurationExtensions.cs`).

### Embedding Deployment Names
Aspire deployment names (e.g., `"embedding"`) ≠ Azure model deployment names (e.g., `"text-embedding-3-small"`). Map via:
```csharp
openai.AddDeployment("embedding", "text-embedding-3-small", "1");
// Later: .WithEnvironment("...__EmbeddingDeployment", embeddingDeployment.Resource.Name)
```

### Azure Search Index Schema Changes
Index must be deleted and recreated if schema changes (field types, vector dimensions). Use `SearchIndexInitializer` which recreates on startup in development.

### Semantic Chunker Token Limits
Exceeding `MaxTokens: 1000` throws during chunking. Increase limit or improve splitting logic for very large documents.

## Testing Strategy

- **Integration tests**: Use `Aspire.Hosting.Testing` to spin up AppHost
- **Unit tests**: Mock `IKnowledgeStore`, `IChatClient`, `IEmbeddingGenerator`
- **Agent tests**: Verify tool registration and invocation (see `AgentFramework/AgentServiceTests.cs`)
- **Workflow tests**: Test multi-agent orchestration (see `Workflows/WorkflowTests.cs`)

## Authentication

All Azure services use `DefaultAzureCredential`:
1. **Local development**: Azure CLI (`az login`)
2. **Production**: Managed Identity (preferred) or Service Principal

API keys supported but deprecated (`IsLocalAuthDisabled = true` in AppHost).
