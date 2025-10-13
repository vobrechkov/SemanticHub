# Repository Guidelines

## Project Structure & Module Organization
- `src/SemanticHub.AppHost` orchestrates Aspire and provisions Azure OpenAI chat/embedding deployments plus the Azure AI Search resource.
- `src/SemanticHub.Api` hosts Microsoft Agent Framework agents, tools, and workflows backed by Azure AI Search.
- `src/SemanticHub.IngestionService` owns document chunking, embedding generation (Azure OpenAI), and indexing into Azure AI Search.
- `src/SemanticHub.WebApp` contains the Next.js UI, `src/SemanticHub.Web` retains the Blazor sample, and `src/SemanticHub.Tests` houses xUnit integration suites with helpers in `SemanticHub.ServiceDefaults`.

## Build, Test, and Development Commands
- `dotnet build` validates all .NET services and shared libraries.
- `dotnet run --project src/SemanticHub.AppHost` provisions Azure AI Search/OpenAI (or binds to existing resources) and starts the ingestion service, agent API, and Next.js dev server.
- `dotnet test` or `dotnet test src/SemanticHub.Tests --collect:"XPlat Code Coverage"` execute backend tests, optionally with coverage.
- `npm --prefix src/SemanticHub.WebApp run lint` enforces UI linting; `npm --prefix src/SemanticHub.WebApp run build` confirms the production bundle.

## Coding Style & Naming Conventions
- Use four-space indentation, `PascalCase` types/members, `camelCase` locals, and keep namespaces aligned with folders (e.g., `SemanticHub.Api.Workflows`).
- Prefer `record` for immutable models and `I*` interface prefixes; centralize configuration extensions in `SemanticHub.ServiceDefaults`.
- TypeScript follows ESLint/Next defaults with 2-space indent, `PascalCase` components, `camelCase` hooks/functions; run `dotnet format` and `npm run lint` before committing.

## Testing Guidelines
- Place xUnit tests in `<Feature>Tests.cs` files, using `Method_Scenario_Result` naming; reuse Aspire fixtures to stub external dependencies.
- Cover cross-service flows (ingestion → indexing → agent queries) with integration tests and assert `/health` endpoints when wiring new services.
- Front-end changes must pass `npm run lint`; add Playwright or Storybook checks when UI logic expands.

## Commit & Pull Request Guidelines
- Write short, imperative commit subjects (e.g., `Add pgvector provisioning`), mirroring history such as `feature/kernel-memory-service (#2)`.
- Scope commits to one concern and explain risks or rollout notes in the body.
- PRs should outline purpose, impacted services, local verification steps (`dotnet run`, `npm run lint`), and any configuration updates or screenshots.

## Architecture & Environment Tips
- Aspire configures Azure Search and Azure OpenAI deployments (`chat`, `embedding`); adjust names in `AppHost.cs` when targeting new environments.
- The ingestion service expects the Azure Search index schema defined in `SearchIndexInitializer`; keep field names aligned with `AgentFramework` configuration.
- Agent tooling obtains base addresses via service discovery; avoid hard-coding ingestion/API URLs in the web app.
