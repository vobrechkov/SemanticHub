# SemanticHub

A sample application that demonstrates a Retrieval Augmented Generation (RAG) architecture for the Microsoft Agent Framework (MAF) using Azure AI Search as the memory store and Azure OpenAI for both chat and embeddings. The solution is orchestrated with .NET Aspire for repeatable local and cloud deployments.

## Overview

The original version of this repository relied on Microsoft Kernel Memory as an external service. The current iteration replaces that dependency with a modern, MAF-aligned data plane built on:

- **Azure AI Search** for hybrid + vector retrieval
- **Azure OpenAI** for chat completions and embedding generation
- **Azure-powered ingestion pipeline** implemented in .NET for chunking, embedding, and indexing content

## Architecture

The solution consists of the following projects:

- **SemanticHub.AppHost** – .NET Aspire AppHost responsible for provisioning Azure resources (Azure AI Search, Azure OpenAI) and wiring up service discovery.
- **SemanticHub.Api** – The main REST API hosting Microsoft Agent Framework agents, knowledge tools, and multi-agent workflows.
- **SemanticHub.IngestionService** – Service responsible for ingesting Markdown and other sources, generating embeddings with Azure OpenAI, and indexing content into Azure AI Search.
- **SemanticHub.Web** – Blazor Server application (legacy UI kept for reference).
- **SemanticHub.WebApp** – Next.js/React dashboard that consumes the agent API.
- **SemanticHub.ServiceDefaults** – Shared Aspire configuration (service discovery, resilience, telemetry).
- **SemanticHub.Tests** – xUnit test suite.

### Service Dependencies

The Aspire AppHost provisions Azure resources and orchestrates the services in the following order:

1. **Azure AI Search** – provisioned with an index tailored for hybrid + vector retrieval.
2. **Azure OpenAI** – chat and embedding deployments (model names configurable via environment variables).
3. **SemanticHub.IngestionService** – depends on Azure AI Search and Azure OpenAI to ingest content.
4. **SemanticHub.Api** – depends on Azure AI Search, Azure OpenAI, and the ingestion service for tool calls.
5. **SemanticHub.WebApp** – depends on the API (and optionally Redis for caching).

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Node.js 20+](https://nodejs.org/) (only required for the `SemanticHub.WebApp` Next.js frontend)
- Azure subscription with permissions to provision:
  - Azure AI Search (Free SKU is sufficient for local development)
  - Azure OpenAI (chat + embeddings deployments)
- Optional: Redis (if you want to enable output caching in the Blazor UI)

## Local Development Setup

1. **Configure Azure resource names** – Edit `src/SemanticHub.AppHost/AppHost.cs` if you want to change the default resource names for Azure AI Search and Azure OpenAI (the sample uses `semhub-eus-dev-*`).
2. **Provide Azure credentials** – Use `dotnet user-secrets` (recommended) or environment variables to provide Azure OpenAI API keys during local development. Aspire service discovery will inject these values into the services.
3. **Optional Redis** – If you want to enable the Blazor UI output cache, ensure a Redis instance is available; Aspire can provision a container automatically when you run the AppHost.

## Getting Started

### 1. Clone and Build

```bash
git clone <repository-url>
cd SemanticKernelMemoryRAG
dotnet build src/SemanticHub.sln
```

### 2. Run the Application

```bash
# Run the entire solution via Aspire AppHost
dotnet run --project src/SemanticHub.AppHost
```

This will start all services with proper dependency management:
1. Azure AI Search (provisioned via Azure resource manager)
2. Azure OpenAI (chat + embeddings deployments)
3. SemanticHub.IngestionService (document ingestion + indexing)
4. SemanticHub.Api (agent endpoints and workflows)
5. SemanticHub.WebApp (Next.js frontend)

### 3. Access the Services

- **Aspire Dashboard**: `https://localhost:<aspire-port>`
- **Agent API (SemanticHub.Api)**: `https://localhost:<api-port>` – provides `/api/agents` and `/api/workflows` endpoints with Scalar API reference in development.
- **Ingestion Service**: `https://localhost:<ingestion-port>` – exposes `/ingestion/markdown` for document ingestion.
- **WebApp (Next.js)**: `http://localhost:3000` – the modern UI served via the NPM app resource.
- **Blazor Web (optional)**: `https://localhost:<web-port>` – legacy UI retained for comparison.

## API Endpoints

### SemanticHub.Api (Agent + Workflow Endpoints)

- `POST /api/agents/chat` – one-shot conversations with the default agent (tool execution allowed).
- `POST /api/agents/chat/stream` – streaming variant for real-time updates.
- `POST /api/workflows/ingest` – runs the multi-agent ingestion workflow (validation → extraction → indexing → verification).
- `POST /api/workflows/research` – full research workflow (search → analysis → synthesis).
- `POST /api/workflows/research/fast` – two-step quick research workflow.
- `GET /api/agents/health` & `GET /api/workflows/health` – health checks for Aspire.

Interactive documentation is available via [Scalar](https://scalar.com/) in development at the API root.

### SemanticHub.IngestionService

- `POST /ingestion/markdown` – Accepts Markdown content (with optional YAML frontmatter) and indexes it into Azure AI Search. The response includes document identifiers and chunk counts.
- Additional ingestion surfaces (web scraping, OpenAPI conversion) are stubbed for future expansion.

The ingestion service also exposes health endpoints through Aspire's `MapDefaultEndpoints` when running in development.

## Configuration

### Development Configuration

The solution uses environment-specific configuration files:

- `appsettings.json` - Base configuration
- `appsettings.Development.json` - Development overrides

### Agent API Configuration

`appsettings.json` under `SemanticHub.Api` exposes an `AgentFramework` section:

- `AzureOpenAI` – endpoint, chat deployment, embedding deployment, optional API key.
- `DefaultAgent` – default name/instructions/model when the caller does not supply overrides.
- `Memory.AzureSearch` – index name, field mapping (key/content/title/summary/vector), semantic configuration name, k-nearest-neighbour setting, and thresholds for relevance.

### Ingestion Service Configuration

`SemanticHub.IngestionService/appsettings.json` contains the ingestion pipeline settings:

- `AzureOpenAI` – embedding deployment used to vectorise chunks.
- `AzureSearch` – index/schema metadata used during index creation and document uploads.
- `Chunking` – parameters for the semantic chunker (target tokens, max tokens, overlap).

Both services honour environment variables supplied by Aspire when you run the AppHost (e.g. `AgentFramework__AzureOpenAI__Endpoint`, `Ingestion__AzureSearch__IndexName`).

### Environment Configuration

- **Local development** – Provide Azure OpenAI credentials through user secrets or environment variables. Aspire will provision Azure AI Search locally via the Azure provisioning SDK or use existing resources if they already exist.
- **Production** – Configure MSI/Managed Identity or service principals. Both services fall back to `DefaultAzureCredential` when an API key is not provided.

## Development Commands

### Build and Run
```bash
# Build the entire solution
dotnet build src/SemanticHub.sln

# Run the AppHost (starts all services via Aspire)
dotnet run --project src/SemanticHub.AppHost

# Run individual services (for development)
dotnet run --project src/SemanticHub.Api
dotnet run --project src/SemanticHub.IngestionService
dotnet run --project src/SemanticHub.Web
```

### Testing
```bash
# Run all tests
dotnet test src/SemanticHub.Tests

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

## Troubleshooting

### Azure Provisioning Issues

1. **Resource name conflicts** – ensure the names configured in `AppHost.cs` are globally unique within the target Azure subscription.
2. **Insufficient permissions** – AppHost provisioning requires `Contributor` (or more granular) permissions for Azure AI Search and Azure OpenAI.
3. **Model availability** – confirm the chosen chat/embedding models are available in the Azure OpenAI region you selected.

### Service Discovery Issues

1. Confirm that Aspire reports all dependent resources as healthy in the dashboard.
2. When running services individually, ensure the `ASPNETCORE_ENVIRONMENT` and connection strings match the expected index names and deployments.
3. Inspect application logs for HTTP tool call failures (e.g. ingestion service returning errors if the index is missing).

### Build Issues

If you encounter build errors:

1. Ensure .NET 9 SDK is installed
2. Clean and rebuild: `dotnet clean && dotnet build`
3. Check individual project builds

## Architecture Notes

- Uses .NET Aspire for service orchestration, provisioning, and health checks.
- Azure AI Search hosts the knowledge index (semantic + vector search).
- Azure OpenAI supplies chat and embedding deployments used by both the agent and ingestion pipeline.
- Redis (optional) provides output caching for the Blazor UI.
- The Web project uses Blazor Server; the WebApp uses Next.js.
- Services communicate via HTTP with Aspire service discovery.
- OpenTelemetry is configured for observability across services.
- Health checks are available via Aspire's default endpoints in development.

## User Interfaces

The solution provides two UI options:

### Blazor Server (SemanticHub.Web)
- **Technology**: Blazor Server with SignalR
- **Features**: Server-side rendering, real-time updates
- **Port**: Dynamic (check Aspire dashboard)

### Next.js/React (SemanticHub.WebApp)
- **Technology**: Next.js 15 + React 19 + Bootstrap 5
- **Features**: Modern React ecosystem, SSR/CSR, SWR data fetching
- **Port**: 3000
- **Documentation**: See [QUICKSTART.md](src/SemanticHub.WebApp/QUICKSTART.md)
- **Migration Guide**: See [MIGRATION.md](MIGRATION.md)

Both UIs provide the same core functionality:
- Home/Welcome page
- Interactive counter demonstration
- Weather forecast data display

## Key Features

- **Modern MAF-aligned RAG** – Azure AI Search provides hybrid/vector retrieval backed by Azure OpenAI embeddings.
- **Agent-aware ingestion** – Dedicated ingestion service handles chunking, embedding, and indexing with configurable chunking strategy.
- **Tool-enabled agents** – Agents can search, list, check status, and ingest content through registered Microsoft Agent Framework tools.
- **Service discovery & resilience** – Aspire manages resource provisioning, health checks, resilience policies, and connection strings.
- **Observability** – OpenTelemetry tracing/metrics and Scalar-based API docs for all HTTP services.
- **Polyglot UI** – Blazor Server and Next.js frontends consuming the same agent/back-end stack.
