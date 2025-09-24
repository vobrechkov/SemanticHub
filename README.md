# SemanticHub

A sample application that explores integrating Microsoft Kernel Memory as the memory store for Microsoft Semantic Kernel using the "memory as a service" architecture pattern.

## Overview

This solution demonstrates how to implement Microsoft Kernel Memory as a dedicated service within a .NET Aspire application. The solution uses .NET Aspire for orchestration and service discovery, providing a scalable architecture for AI applications.

## Architecture

The solution consists of several interconnected services:

- **SemanticHub.AppHost** - .NET Aspire AppHost that orchestrates all services, handles service discovery, and manages dependencies
- **SemanticHub.KernelMemoryService** - Dedicated service that provides Kernel Memory functionality as a web service
- **SemanticHub.KnowledgeApi** - Web API service that exposes knowledge/memory endpoints and proxies requests to KernelMemoryService
- **SemanticHub.Web** - Blazor Server UI that provides the frontend interface
- **SemanticHub.ServiceDefaults** - Shared service configuration and extensions
- **SemanticHub.Tests** - xUnit test project for integration testing

### Service Dependencies

The AppHost defines the following service dependency chain:
1. Azurite storage emulator (for document storage and queues)
2. PostgreSQL with pgvector (for vector storage and memory database)
3. Azure OpenAI (for text generation and embeddings)
4. Redis cache (for Web UI caching)
5. KernelMemoryService depends on Azurite, PostgreSQL, and Azure OpenAI
6. KnowledgeApi depends on KernelMemoryService
7. Web UI depends on Redis and KnowledgeApi

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop) (for Azurite, PostgreSQL, and Redis containers)
- Azure OpenAI account and deployment (for text generation and embeddings)

## Local Development Setup

### 1. Azurite Storage Emulator

The Azurite storage emulator is automatically managed by .NET Aspire. When you run the AppHost, it will:

- Automatically pull and start the `mcr.microsoft.com/azure-storage/azurite` container
- Configure blob storage endpoints for document storage
- Provide a local development environment that mimics Azure Storage

**No manual setup required** - Aspire handles container lifecycle management automatically.

#### Accessing Azurite

Once running, you can access Azurite through:
- **Blob Service**: `http://127.0.0.1:<dynamic-port>`
- **Azure Storage Explorer**: Automatically discovers Azurite on default ports
- **Aspire Dashboard**: Monitor container status and logs

### 2. PostgreSQL with pgvector

PostgreSQL with pgvector extension is automatically managed by Aspire for vector storage and memory database operations. The service uses the `ankane/pgvector:latest` image which includes the pgvector extension for efficient vector similarity searches.

### 3. Redis Cache

Redis is automatically managed by Aspire for Web UI output caching.

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
1. Azurite storage emulator
2. PostgreSQL with pgvector
3. Redis cache
4. KernelMemoryService
5. KnowledgeApi
6. Web UI

### 3. Access the Services

- **Aspire Dashboard**: `https://localhost:<aspire-port>` (check console output)
- **Web UI**: `https://localhost:<web-port>` (check console output)
- **KnowledgeApi**: `https://localhost:<api-port>` (check console output)
- **KernelMemoryService**: `https://localhost:<memory-port>` (check console output)

## API Endpoints

### KernelMemoryService

The KernelMemoryService exposes the Microsoft Kernel Memory endpoints under `/api/memory`:

- `POST /api/memory/upload` - Upload documents (text or files)
- `POST /api/memory/ask` - Ask questions about stored documents
- `POST /api/memory/search` - Search through stored knowledge
- `GET /api/memory/documents/{id}/status` - Check document processing status
- `DELETE /api/memory/documents/{id}` - Delete documents
- `GET /api/memory/index` - List available indexes

### KnowledgeApi

The KnowledgeApi provides a simplified interface that proxies requests to KernelMemoryService:

- `POST /knowledge/upload/text` - Upload text documents
- `POST /knowledge/ask` - Ask questions about documents
- `POST /knowledge/search` - Search knowledge base
- `GET /knowledge/documents/{id}/status` - Get document processing status

### Interactive API Documentation

- **KernelMemoryService**: Available at the service root URL (uses Scalar API Reference)
- **KnowledgeApi**: Available at `/swagger` (development only)

## Configuration

### Development Configuration

The solution uses environment-specific configuration files:

- `appsettings.json` - Base configuration
- `appsettings.Development.json` - Development overrides

### Storage Configuration

- **Document Storage**: Azurite blob storage (local development)
- **Vector Storage**: PostgreSQL with pgvector extension
- **Message Queues**: Azure Storage Queues (via Azurite emulator)
- **Cache**: Redis (for Web UI output caching)

### AI Services Configuration

The solution requires Azure OpenAI configuration:

- **Text Generation**: GPT-4o-mini (configurable deployment name: `gpt-4o-mini`)
- **Embeddings**: Text-embedding-3-small (configurable deployment name: `text-embedding-3-small`)
- **Authentication**: Azure Identity (for production) or connection strings (for development)

### Environment Configuration

Environment-specific configuration is handled through:

- **AppHost**: Automatically configures connection strings and environment variables
- **Development**: Uses connection strings for local emulators
- **Production**: Uses Azure Identity for secure authentication

## Development Commands

### Build and Run
```bash
# Build the entire solution
dotnet build src/SemanticHub.sln

# Run the AppHost (starts all services via Aspire)
dotnet run --project src/SemanticHub.AppHost

# Run individual services (for development)
dotnet run --project src/SemanticHub.Web
dotnet run --project src/SemanticHub.KnowledgeApi
dotnet run --project src/SemanticHub.KernelMemoryService
```

### Testing
```bash
# Run all tests
dotnet test src/SemanticHub.Tests

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

## Troubleshooting

### Container Issues

If you encounter container-related issues:

1. **Containers not starting**: Check Docker Desktop is running
2. **Port conflicts**: Aspire will automatically assign available ports
3. **Azurite connectivity**: Storage Explorer should auto-discover Azurite
4. **PostgreSQL connectivity**: Check that pgvector image pulled successfully (`ankane/pgvector:latest`)

### Service Discovery Issues

If services can't communicate:

1. Check the Aspire dashboard for service status
2. Verify all services are running and healthy
3. Check service logs in the Aspire dashboard

### Build Issues

If you encounter build errors:

1. Ensure .NET 9 SDK is installed
2. Clean and rebuild: `dotnet clean && dotnet build`
3. Check individual project builds

## Architecture Notes

- Uses .NET Aspire for service orchestration and health checks
- Azurite provides local Azure Storage emulation for documents and queues
- PostgreSQL with pgvector extension handles vector storage and memory database
- Redis provides output caching for the Web UI
- The Web project uses Blazor Server with interactive server components
- Services communicate via HTTP with service discovery handled by Aspire
- OpenTelemetry is configured for observability across services
- Health checks are implemented at `/health` endpoints
- Microsoft Kernel Memory runs as a dedicated service with full API exposure

## Key Features

- **Memory as a Service**: Kernel Memory runs as an independent, scalable service
- **Service Discovery**: Automatic service resolution via Aspire
- **Local Development**: Zero-configuration local setup with emulators and containers
- **Vector Search**: PostgreSQL with pgvector for efficient similarity searches
- **Distributed Processing**: Azure Storage Queues for scalable document processing
- **AI Integration**: Azure OpenAI for text generation and embeddings
- **Observability**: Full OpenTelemetry integration with Aspire dashboard
- **Interactive Documentation**: Scalar API Reference for KernelMemoryService
- **Scalability**: Services can be scaled independently
- **Modern Architecture**: .NET 9, Aspire, and cloud-native patterns
