# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

This is a .NET 9 solution that demonstrates a Microsoft Agent Framework (MAF) RAG stack using Azure AI Search as the knowledge store and Azure OpenAI for chat/embeddings. .NET Aspire handles orchestration and service discovery.

The solution implements a modern, domain-driven architecture for ingesting various document types (HTML, Markdown, OpenAPI specs, and web pages via sitemaps) and making them searchable through intelligent agents.

## Project Structure

The solution consists of several interconnected services:

- **SemanticHub.AppHost** – .NET Aspire AppHost that provisions Azure AI Search, Azure OpenAI, and Azure Blob Storage resources with service discovery (`AppHost.cs`).
- **SemanticHub.Api** – Agent-facing API exposing chat endpoints, tools, and workflows powered by Microsoft Agent Framework.
- **SemanticHub.IngestionService** – Document ingestion pipeline implementing DDD patterns with specialized workflows for different content types.
- **SemanticHub.WebApp** – Next.js/React UI for interacting with the agent API.
- **SemanticHub.ServiceDefaults** – Shared service configuration and extensions (telemetry, resilience, health checks).
- **SemanticHub.Tests** – xUnit test project for unit and integration testing with comprehensive workflow coverage.

## Service Dependencies

The AppHost defines the following service dependency chain:
1. **Azure AI Search** – Free tier search service for vector and semantic search
2. **Azure OpenAI** – Provisions `gpt-4o-mini` (chat) and `text-embedding-3-small` (embeddings) deployments
3. **Azure Blob Storage** – Runs Azurite emulator locally for document storage
4. **IngestionService** – Depends on Azure Search + Azure OpenAI + Blob Storage
5. **SemanticHub.Api** – Depends on Azure Search + Azure OpenAI + IngestionService + Blob Storage
6. **WebApp** – Depends on the Agent API

### Optional OpenSearch Support
The AppHost supports optional OpenSearch (containerized) as an alternative to Azure AI Search via feature flags in configuration.

## Development Commands

### Build and Run
```bash
# Build the entire solution
dotnet build

# Run the AppHost (starts all services via Aspire)
dotnet run --project src/SemanticHub.AppHost

# Access the Aspire dashboard at https://localhost:17283
```

### Testing
```bash
# Run all tests
dotnet test

# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run specific test project
dotnet test src/SemanticHub.Tests

# Run specific workflow tests
dotnet test --filter "FullyQualifiedName~IngestionWorkflow"
```

## Architecture Overview

### Ingestion Service Architecture (DDD Refactored)

The ingestion service has been refactored following Domain-Driven Design principles with clear separation of concerns:

**Domain Layer** (`Domain/`)
- **Aggregates**: `IngestionRequests.cs`, `SitemapIngestionSettings.cs` – Core business entities
- **Results**: `IngestionResult.cs`, `SitemapIngestionResult.cs`, `OpenApiIngestionOutcome.cs` – Workflow outcomes
- **Ports**: Interface contracts for external dependencies
- **Resources**: Domain-specific data structures for OpenAPI, sitemaps, and documents
- **Mappers**: Translation between request DTOs and domain aggregates

**Application Layer** (`Application/Workflows/`)
- `MarkdownIngestionWorkflow.cs` – Processes single Markdown documents
- `HtmlIngestionWorkflow.cs` – Processes single HTML documents
- `WebPageIngestionWorkflow.cs` – Scrapes and processes web pages
- `SiteMapIngestionWorkflow.cs` – Crawls sitemaps and ingests discovered URLs
- `BulkMarkdownIngestionWorkflow.cs` – Batch processes documents from blob storage
- `OpenApiIngestionWorkflow.cs` – Parses and ingests OpenAPI specifications

**Services Layer** (`Services/`)
- **Processors**: `HtmlProcessor`, `MarkdownProcessor` – Content transformation
- **Scraping**: `PlaywrightHtmlScraper` – Resilient web scraping with retry/backoff
- **Sitemaps**: `HttpSitemapFetcher`, `XmlSitemapParser`, `SitemapUrlFilterPolicy`, `DefaultChangeFrequencyHeuristic` – Sitemap crawling infrastructure
- **OpenApi**: `OpenApiSpecLocator`, `OpenApiSpecParser`, `OpenApiMarkdownGenerator`, `OpenApiDocumentSplitter` – OpenAPI processing pipeline
- **Core**: `SemanticChunker`, `AzureOpenAIEmbeddingService`, `AzureSearchIndexer`, `BlobStorageService` – Shared infrastructure

### Agent API Architecture

The API exposes Microsoft Agent Framework (MAF) agents with:
- RAG-powered chat via Azure OpenAI
- Knowledge base tools backed by Azure AI Search vector search
- Document ingestion endpoints proxying to the IngestionService
- OpenTelemetry tracing and health checks

## Key Configuration

**IMPORTANT**: Always check the actual `appsettings.json` files for current configuration values. Do not rely solely on this documentation.

### Ingestion Service Configuration

Configuration is defined in [src/SemanticHub.IngestionService/appsettings.json](src/SemanticHub.IngestionService/appsettings.json) under the `Ingestion:` section.

**Configuration Structure** (defined in [src/SemanticHub.IngestionService/Configuration/IngestionOptions.cs](src/SemanticHub.IngestionService/Configuration/IngestionOptions.cs)):

- **`AzureOpenAI:`** - Azure OpenAI configuration
  - `Endpoint`, `EmbeddingDeployment`, `ApiKey`
  - Note: Aspire injects these values at runtime via service discovery

- **`AzureSearch:`** - Azure AI Search index configuration
  - `Endpoint`, `ApiKey`, `IndexName` (default: "knowledge-index")
  - `VectorDimensions` (1536 for text-embedding-3-small)
  - `VectorKNearestNeighbors` (default: 8)
  - Field mappings: `KeyField`, `ContentField`, `TitleField`, `VectorField`, etc.

- **`Chunking:`** - Document chunking behavior
  - `TargetTokenCount` (512), `MaxTokenCount` (1024), `OverlapPercentage` (0.1)

- **`BlobStorage:`** - Azure Blob Storage for bulk ingestion
  - `Endpoint`, `ConnectionString`, `DefaultContainer` (default: "documents")

- **`Sitemap:`** - Sitemap crawling limits and behavior
  - `MaxPages` (200), `MaxDepth` (2), `MaxConcurrency` (3)
  - `ThrottleMilliseconds` (250), `RespectRobotsTxt` (true)
  - `UserAgent`, `FetchTimeoutSeconds`, `RecencyHalfLifeDays`, etc.

- **`OpenApi:`** - OpenAPI-specific processing
  - `MaxMarkdownSegmentLength` (8000) - Controls splitting of large specs

### Agent API Configuration

Configuration is defined in [src/SemanticHub.Api/appsettings.json](src/SemanticHub.Api/appsettings.json) under the `AgentFramework:` section.

**Configuration Structure**:

- **`AzureOpenAI:`** - Azure OpenAI for chat and embeddings
  - `Endpoint`, `ChatDeployment` (gpt-4o-mini), `EmbeddingDeployment`, `ApiKey`

- **`DefaultAgent:`** - Default agent configuration
  - `Name`, `Instructions`, `Model`

- **`Memory:`** - Knowledge base and memory configuration
  - `Provider` - "AzureSearch" or "OpenSearch"
  - `MaxResults` (5), `MinRelevance` (0.6)
  - `EnableMem0` (false), `EnableWhiteboard` (true)
  - `AzureSearch:` - Azure AI Search settings (same schema as ingestion service)
  - `OpenSearch:` - OpenSearch settings for alternative vector store

## Ingestion Workflows

### Available Endpoints

- `POST /ingestion/markdown` – Ingest single Markdown document
- `POST /ingestion/html` – Ingest single HTML document
- `POST /ingestion/webpage` – Scrape and ingest web page
- `POST /ingestion/sitemap` – Crawl sitemap and ingest discovered pages
- `POST /ingestion/openapi` – Parse and ingest OpenAPI specification
- `POST /ingestion/bulk` – Batch process documents from blob storage (auto-detects type)

### Workflow Behavior

1. **Document Processing**: Content is validated, cleaned, and transformed to Markdown
2. **Semantic Chunking**: Documents are split using overlap-aware token-based chunking
3. **Embedding Generation**: Chunks are embedded using Azure OpenAI text-embedding-3-small
4. **Vector Indexing**: Embeddings and metadata are upserted into Azure AI Search
5. **Telemetry**: All workflows emit OpenTelemetry spans, metrics, and structured logs

## Testing Strategy

The solution includes comprehensive test coverage:

- **Unit Tests**: Processor and service logic validation
- **Integration Tests**: Workflow orchestration with mocked dependencies
- **End-to-End Tests**: Full pipeline tests with HTTP clients and live services (conditionally run)
- **Test Fixtures**: Shared test data and mock implementations

Test projects follow the same namespace structure as production code for discoverability.

## Recent Refactoring (Phases 1-3 Completed)

The ingestion service underwent a three-phase DDD refactoring:

**Phase 1 – HTML/Markdown Foundation**
- Replaced monolithic `DocumentIngestionService` with specialized workflows
- Introduced domain layer with aggregates, ports, and outcome types
- Hardened processors with HTML sanitization and structured error handling
- Added workflow-specific integration tests

**Phase 2 – Sitemap Ingestion**
- Implemented sitemap crawling with URL prioritization and throttling
- Added `robots.txt` enforcement and change-frequency heuristics
- Exposed `/ingestion/sitemap` endpoint with configurable limits
- Comprehensive sitemap parsing and filtering tests

**Phase 3 – OpenAPI Ingestion**
- Built OpenAPI processing pipeline (spec locator, parser, Markdown generator, splitter)
- Integrated OpenAPI workflow into bulk ingestion with auto-detection
- Added configurable Markdown segment splitting for large specs
- End-to-end OpenAPI ingestion tests with HTTP-hosted specs

## Development Notes

- All services call `builder.AddServiceDefaults()` for service discovery, resilience, and telemetry
- Aspire uses `https+http://` URL scheme for HTTPS preference when available
- OpenTelemetry instrumentation is enabled by default in development
- Health checks are exposed at `/health` and `/alive` endpoints
- Scalar API documentation is available in development mode
- The solution uses `ILogger` for structured logging throughout
- Playwright requires browser binaries: `pwsh bin/Debug/net9.0/playwright.ps1 install`

## Architectural Principles

- **Domain-Driven Design**: Clear boundaries between domain, application, and infrastructure
- **Single Responsibility**: Each workflow handles one ingestion concern
- **Dependency Inversion**: Workflows depend on ports (interfaces), not implementations
- **Observability First**: Telemetry spans, metrics, and logs at all layers
- **Resilience**: Retry policies with exponential backoff for external services
- **Testability**: Workflows are tested with both unit and integration strategies
