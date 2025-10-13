# Repository Guidance

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

This is a .NET 9 solution that demonstrates a Microsoft Agent Framework (MAF) Retrieval Augmented Generation stack backed by Azure AI Search and Azure OpenAI. .NET Aspire handles service discovery, resource provisioning, and orchestration.

## Project Structure

The solution consists of several interconnected services:

- **SemanticHub.AppHost** – .NET Aspire AppHost that provisions Azure AI Search/OpenAI resources and coordinates all service dependencies (`AppHost.cs`).
- **SemanticHub.Api** – Agent-facing API exposing chat endpoints, tools, and multi-agent workflows.
- **SemanticHub.IngestionService** – Ingestion pipeline that chunks content, generates embeddings with Azure OpenAI, and indexes documents into Azure AI Search.
- **SemanticHub.Web** – Blazor Server UI (legacy sample).
- **SemanticHub.WebApp** – Next.js/React UI.
- **SemanticHub.ServiceDefaults** – Shared service configuration and extensions.
- **SemanticHub.Tests** – xUnit integration/unit tests.

## Service Dependencies

The AppHost defines the following service dependency chain:
1. Azure AI Search (index provisioning)
2. Azure OpenAI (chat + embedding deployments)
3. SemanticHub.IngestionService depends on Azure Search + Azure OpenAI
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

- Aspire coordinates Azure Search/OpenAI provisioning and injects service discovery connection strings.
- Ingestion service handles semantic chunking + embedding and pushes documents into Azure AI Search.
- Agents call registered tools to query Azure AI Search and initiate ingestion tasks.
- Redis caching for the Blazor UI remains optional.
- OpenTelemetry and Scalar API docs are available in development.
- Health checks surface via Aspire's default endpoints (`/health`, `/alive`) in development.

## Key Configuration

- All services call `builder.AddServiceDefaults()` for shared Aspire configuration.
- Agent API configuration lives under `AgentFramework` (Azure OpenAI + Azure Search).
- Ingestion service configuration lives under `Ingestion` (Azure OpenAI embeddings, Azure Search index schema, chunking parameters).
- Service discovery uses `https+http://` scheme for HTTPS preference; dependent services wait for prerequisites before starting.
