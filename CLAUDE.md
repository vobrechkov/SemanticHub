# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

This is a .NET 9 solution that explores integrating Microsoft Kernel Memory as the memory store for Microsoft Semantic Kernel. The solution uses .NET Aspire for orchestration and service discovery.

## Project Structure

The solution consists of several interconnected services:

- **SemanticHub.AppHost** - .NET Aspire AppHost that orchestrates all services, handles service discovery, and manages dependencies. Contains the main orchestration logic in `AppHost.cs`.
- **SemanticHub.KernelMemoryService** - Service that provides Kernel Memory functionality (memory store integration)
- **SemanticHub.KnowledgeApi** - Web API service that exposes knowledge/memory endpoints
- **SemanticHub.Web** - Blazor Server UI that provides the frontend interface
- **SemanticHub.ServiceDefaults** - Shared service configuration and extensions
- **SemanticHub.Tests** - xUnit test project for integration testing

## Service Dependencies

The AppHost defines the following service dependency chain:
1. Redis cache (foundational dependency)
2. KernelMemoryService depends on Redis
3. KnowledgeApi depends on KernelMemoryService
4. Web UI depends on both Redis and KnowledgeApi

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

- Uses .NET Aspire for service orchestration and health checks
- Redis is used for caching and potentially as a backing store
- The Web project uses Blazor Server with interactive server components
- Services communicate via HTTP with service discovery handled by Aspire
- OpenTelemetry is configured for observability across services
- Health checks are implemented at `/health` endpoints

## Key Configuration

- All services use `builder.AddServiceDefaults()` for common Aspire configuration
- Redis output caching is configured in the Web project
- Service discovery uses `https+http://` scheme for HTTPS preference
- The AppHost waits for dependent services before starting dependent ones