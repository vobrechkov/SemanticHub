# Problem

There were many cross cutting concerns in original DocumentIngestionService. A multi-phased initiative to replace the service with multiple smaller services following DDD and SRP principles is being implemented.

## Proposed Services

HtmlScraper (or WebScraper or similar) - handles scraping of web pages - uses Playwright
HtmlProcessor - handles all HTML tasks. Uses HtmlAgilityPack.
MarkdownProcessor - handles Markdown processing taks.
MarkdownConverter - converts HTML into Markdown - uses ReverseMarkdown
OpenApiProcessor - handles OpenApi tasks
Additional services we may need:

Service to work with sitemaps
Other services
I want to explore using MAF Workflows for the various ingestions flows:

## Proposed Workflows 

SiteMapIngestionWorkflow (ingests all web pages of a website from a sitemap)
WebPageInestionWorkflow (scrapes and ingests a single web page)
HtmlIngestionWorkflow (ingests a single HTML document)
BulkMarkdownIngestionWorkflow (reads Markdown documents in bulk from blob storage)
MarkdownIngestionWorkflow (ingests a single Markdown document)
Some initial work was done but these requirements were not fully satisfied.
I want you to complete the work as described here following proper architectural patterns, SRP, DDD, unit tests.

We will follow a 3-phase approach:

- Phase 1 – HTML/Markdown Foundation **Delivered**
- Phase 2 – Sitemap Ingestion **Delivered**
- Phase 3 – OpenAPI Ingestion **Delivered**

## Changelog:

**Phase 1 (Completred)**:

- Introduced ingestion domain layer (src/SemanticHub.IngestionService/Domain/...) with aggregates, error codes, ports, and outcomes so workflows no longer depend on service classes.
- Replaced Playwright WebScraperTool with resilient PlaywrightHtmlScraper (Services/Scraping/PlaywrightHtmlScraper.cs) that adds retry/backoff, structured telemetry, and sitemap/recursive helpers.
- Removed the monolithic DocumentIngestionService and wired new orchestrators (Application/Workflows/*IngestionWorkflow.cs) through DI and IngestionEndpoints.cs; each wkflow now emits activity spans/metrics and composes the appropriate processors.
- Hardened processors and services: HtmlProcessor now sanitizes with HtmlAgilityPack, MarkdownProcessor consumes the shared converter interface, and IBlobStorageService interface enables mocking & reuse.
- Added focused workflow/E2E tests under SemanticHub.Tests/Workflows/…; dotnet test (31 passed, 2 skipped) confirms the new pipeline, while KnowledgeBaseToolsTests.ListDocuments_ReturnsFormattedList is temporarily skipped pending a null-handling fix.

**Phase 2 (Completed)**:

- Added first-class sitemap ingestion primitives: config now exposes `SitemapIngestionOptions` alongside new domain records and outcomes (`Configuration/IngestionOptions.cs:18`, `Domain/Aggregates/SitemapIngestionSettings.cs:1`, `Domain/Results/SitemapIngestionResult.cs:1`, `Domain/Sitemaps/*`).
- Implemented the orchestrator that discovers sitemap trees, scores URLs, throttles scrapes, and records telemetry (`Application/Workflows/SiteMapIngestionWorkflow.cs:32`) with supporting counters in `Diagnostics/IngestionTelemetry.cs:43`.
- Delivered fetch/parse/filter services with HTTP resilience, `robots.txt` enforcement, and change-frequency heuristics (`Services/Sitemaps/HttpSitemapFetcher.cs:1`, `XmlSitemapParser.cs:1`, `SitemapUrlFilterPolicy.cs:`1, `DefaultChangeFrequencyHeuristic.cs:1)`.
- Exposed `/ingestion/sitemap` and wired the new workflow + typed clients into DI (`Endpoints/IngestionEndpoints.cs:49`, `Extensions/IngestionServiceExtensions.cs:87`, `Domain/Mappers/IngestionRequestMapper.cs:61`, `Models/SitemapIngestionRequest.cs:1`).
- Backed the change with targeted unit coverage and an end-to-end crawl scenario (`SemanticHub.Tests/Sitemaps/*.cs`, `SemanticHub.Tests/Workflows/SiteMapIngestionWorkflowTests.cs:1`, `SemanticHub.Tests/Workflows/IngestionEndToEndTests.cs:93`) plus authored `docs/SITEMAP_INGESTION.md` explaining usage.
- Tests: dotnet test `src/SemanticHub.sln`

**Phase 3 (Completed)**:

- Added the OpenAPI domain aggregate and mapping pipeline so requests stay inside the new DDD boundary (`src/SemanticHub.IngestionService/Domain/Aggregates/IngestionRequests.cs:42`, `src/SemanticHub.IngestionService/Domain/Mappers/IngestionRequestMapper.cs:74`, `src/SemanticHub.IngestionService/Domain/Resources/IngestionResource.cs:46`) together with reusable spec/document records (`src/SemanticHub.IngestionService/Domain/OpenApi/OpenApiDocuments.cs:8`).
Replaced the monolithic processor with focused services—spec locator, parser, markdown generator, and splitter (`src/SemanticHub.IngestionService/Services/OpenApi/OpenApiSpecLocator.cs#L17`, `.../OpenApiSpecParser.cs:13`, `.../OpenApiMarkdownGenerator.cs:12`, `.../OpenApiDocumentSplitter.cs:12`)—and the new `OpenApiIngestionWorkflow` orchestrator that drives Markdown ingestion and telemetry (`src/SemanticHub.IngestionService/Application/Workflows/OpenApiIngestionWorkflow.cs:19`). The legacy `OpenApiProcessor` and tool have been removed.
Wired the new workflow throughout the service: DI registration (`src/SemanticHub.IngestionService/Extensions/IngestionServiceExtensions.cs:81`), endpoint handler (`src/SemanticHub.IngestionService/Endpoints/IngestionEndpoints.cs:82`), and bulk blob ingestion path (`src/SemanticHub.IngestionService/Application/Workflows/BulkMarkdownIngestionWorkflow.cs:20`) so OpenAPI files are validated and routed through the workflow.
Expanded coverage with parser/markdown integration tests and workflow unit tests (`src/SemanticHub.Tests/OpenApi/OpenApiSpecParserTests.cs:13`, `src/SemanticHub.Tests/Workflows/OpenApiIngestionWorkflowTests.cs:15`) while retiring the old tool tests.
Refreshed documentation to describe the staged OpenAPI pipeline and new components (`docs/OPENAPI_INGESTION.md:17`).
Tests: `dotnet test src/SemanticHub.sln`

**Post Phase 3 (Completed)**:

- Surfaced configurable OpenAPI Markdown splitter limits via `Ingestion:OpenApi:MaxMarkdownSegmentLength`, wiring options through DI (`src/SemanticHub.IngestionService/Configuration/IngestionOptions.cs:18`, `src/SemanticHub.IngestionService/Services/OpenApi/OpenApiDocumentSplitter.cs:13`, `src/SemanticHub.IngestionService/Extensions/IngestionServiceExtensions.cs:91`).
- Added an HTTP-backed end-to-end workflow test to validate locator + splitter behaviour against hosted specs (`src/SemanticHub.Tests/Workflows/OpenApiIngestionEndToEndTests.cs:1`) and documented the new option (`docs/OPENAPI_INGESTION.md:68`, `src/SemanticHub.IngestionService/appsettings.json:55`).
- `dotnet test src/SemanticHub.sln`
- Added strongly typed OpenAPI configuration with OpenApiIngestionOptions so Markdown splitting limits can be tuned via config and validated at startup (src/SemanticHub.IngestionService/Configuration/IngestionOptions.cs:20, src/SemanticHub.IngestionService/Configuration/IngestionOptions.cs:126).
- Reworked the splitter to consume those options (including guardrails) and logged segmentation decisions for observability (src/SemanticHub.IngestionService/Services/OpenApi/OpenApiDocumentSplitter.cs:14).
- Updated DI and sample settings to honour the new knob, enabling overrides through Ingestion:OpenApi:MaxMarkdownSegmentLength (src/SemanticHub.IngestionService/Extensions/IngestionServiceExtensions.cs:87, src/SemanticHub.IngestionService/appsettings.json:55).
- Documented the new configuration and progress in the refactor log (docs/OPENAPI_INGESTION.md:205, docs/ddd-refactor.md:62) and introduced an HTTP-hosted end-to-end workflow test that exercises locator + splitter behaviour (src/SemanticHub.Tests/Workflows/OpenApiIngestionEndToEndTests.cs:18).
- Added bulk ingestion end-to-end coverage so Markdown, HTML, and OpenAPI blobs execute the expected processors concurrently (`src/SemanticHub.Tests/Workflows/BulkMarkdownIngestionEndToEndTests.cs:1`). Tests: `dotnet test src/SemanticHub.sln`

# Next Steps:

- Monitor ingestion telemetry with the expanded test suite and identify any remaining integration gaps.
