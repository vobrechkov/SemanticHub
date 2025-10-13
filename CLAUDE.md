# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

This is a .NET 9 solution that demonstrates a Microsoft Agent Framework (MAF) RAG stack using Azure AI Search as the knowledge store and Azure OpenAI for chat/embeddings. .NET Aspire handles orchestration and service discovery.

## Project Structure

The solution consists of several interconnected services:

- **SemanticHub.AppHost** – .NET Aspire AppHost that provisions Azure AI Search/OpenAI resources and wires service discovery (`AppHost.cs`).
- **SemanticHub.Api** – Agent-facing API exposing chat endpoints, tools, and workflows powered by Microsoft Agent Framework.
- **SemanticHub.IngestionService** – Document ingestion pipeline (chunking, embeddings, Azure AI Search indexing).
- **SemanticHub.Web** – Blazor Server UI (legacy sample).
- **SemanticHub.WebApp** – Next.js/React UI.
- **SemanticHub.ServiceDefaults** – Shared service configuration and extensions.
- **SemanticHub.Tests** – xUnit test project for integration testing.

## Service Dependencies

The AppHost defines the following service dependency chain:
1. Azure AI Search (index provisioning)
2. Azure OpenAI (chat + embeddings deployments)
3. IngestionService depends on Azure Search + Azure OpenAI
4. SemanticHub.Api depends on Azure Search + Azure OpenAI + IngestionService
5. Web/WebApp depend on the API (and optionally Redis for caching)

## Development Commands

### Build and Run
```bash
# Build the entire solution
dotnet build

# Run the AppHost (starts all services via Aspire)
dotnet run --project src/SemanticHub.AppHost
```

### Testing
```bash
# Run all tests
dotnet test

# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run specific test project
dotnet test src/SemanticHub.Tests
```

## Architecture Notes

- Aspire coordinates Azure resource provisioning and injects connection strings for Azure AI Search and Azure OpenAI.
- Ingestion service performs semantic chunking and embedding generation before upserting into Azure AI Search.
- Agents obtain grounding context and retrieve documents using registered AI tools backed by Azure AI Search.
- Redis output caching remains optional for the Blazor UI.
- OpenTelemetry instrumentation and Scalar API docs are enabled in development.
- Health checks are exposed via Aspire's default endpoints (`/health`, `/alive`) in development.

## Key Configuration

- All services call `builder.AddServiceDefaults()` for service discovery/resilience/telemetry.
- Agent API options live under `AgentFramework` (Azure OpenAI + Azure AI Search section).
- Ingestion service options live under `Ingestion` (Azure OpenAI embeddings, Azure AI Search index schema, chunking settings).
- Aspire uses service-discovery URLs with the `https+http://` scheme for HTTPS preference when available.
