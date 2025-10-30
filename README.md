# SemanticHub

Sample RAG solution built with Microsoft Agent Framework (MAF), Azure AI Search, Azure OpenAI, and .NET Aspire.

## Projects

- **SemanticHub.AppHost** – .NET Aspire orchestrator that provisions Azure resources (AI Search, OpenAI, Blob Storage, etc.) and manages service discovery
- **SemanticHub.Api** – Agent API exposing chat endpoints, knowledge base tools, and multi-agent workflows via Microsoft Agent Framework
- **SemanticHub.IngestionService** – Document ingestion pipeline for chunking, embedding generation, and Azure AI Search indexing
- **SemanticHub.WebApp** – Modern Next.js/React UI for agent interaction
- **SemanticHub.ServiceDefaults** – Shared Aspire configuration (telemetry, health checks, resilience policies)
- **SemanticHub.Tests** – xUnit integration and unit test suite

## Aspire

1. **Azure OpenAI** – Chat (`gpt-4o-mini`) and embedding (`text-embedding-3-small`) deployments
2. **Azure AI Search** – Vector + hybrid search index provisioning
3. **Azure Blob Storage** – Document storage (Azurite emulator for local development)
4. **SemanticHub.IngestionService** → depends on Azure OpenAI + Azure AI Search
5. **SemanticHub.Api** → depends on Azure OpenAI + Azure AI Search + Blob Storage + IngestionService
6. **SemanticHub.WebApp** → depends on API

## Azure RBAC

Grant your Azure identity the necessary permissions:

```bash
cd src/SemanticHub.AppHost/Scripts
./setup-all-rbac.sh
```

Wait 5-10 minutes for role assignments to propagate. See [Scripts/README.md](src/SemanticHub.AppHost/Scripts/README.md) for details.

## API Endpoints

### Agent API (`SemanticHub.Api`)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/agents/chat` | POST | Single-turn chat with agent |
| `/api/agents/chat/stream` | POST | Streaming chat responses |
| `/api/workflows/ingest` | POST | Multi-agent ingestion workflow |
| `/api/workflows/research` | POST | Full research workflow |
| `/api/workflows/research/fast` | POST | Quick two-step research |
| `/health` | GET | Health check endpoint |

### Ingestion Service

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/ingestion/markdown` | POST | Ingest Markdown documents with YAML frontmatter |
| `/ingestion/webpage` | POST | Scrape and ingest web page content |
| `/ingestion/openapi` | POST | Parse and ingest OpenAPI specifications (YAML/JSON) |
| `/health` | GET | Health check endpoint |

### Ingesting Documents

**Markdown Documents:**
```bash
curl -X POST https://localhost:<port>/ingestion/markdown \
  -H "Content-Type: application/json" \
  -d '{
    "title": "Getting Started Guide",
    "content": "# Introduction\n\nThis is a sample document...",
    "tags": ["guide", "documentation"]
  }'
```

**Web Pages:**
```bash
curl -X POST https://localhost:<port>/ingestion/webpage \
  -H "Content-Type: application/json" \
  -d '{
    "url": "https://example.com/article",
    "tags": ["article", "web"]
  }'
```

**OpenAPI Specifications:**
```bash
curl -X POST https://localhost:<port>/ingestion/openapi \
  -H "Content-Type: application/json" \
  -d '{
    "specSource": "/path/to/openapi.yaml",
    "documentIdPrefix": "my-api",
    "tags": ["api", "specification"]
  }'
```

*OpenAPI ingestion flow:*
- Parses OpenAPI 3.x specifications (YAML or JSON)
- Converts each endpoint to structured Markdown with YAML frontmatter
- Extracts parameters, request/response schemas, and examples
- Creates searchable documents for each API endpoint
- Supports both local file paths and remote URLs

## Configuration

### Memory Provider Selection

Set the provider in `appsettings.json` or via environment variable:

```bash
# Azure AI Search (default)
AgentFramework__Memory__Provider=AzureSearch

# OpenSearch (local development)
AgentFramework__Memory__Provider=OpenSearch
```

### Configuration Sections

**Agent API**:
```json
{
  "AgentFramework": {
    "AzureOpenAI": {
      "Endpoint": "https://<name>.openai.azure.com/",
      "ChatDeployment": "gpt-4o-mini",
      "EmbeddingDeployment": "text-embedding-3-small"
    },
    "Memory": {
      "Provider": "AzureSearch",
      "AzureSearch": {
        "IndexName": "semantichub-kb",
        "EnableSemanticRanker": true
      }
    }
  }
}
```

**Ingestion Service**:
```json
{
  "Ingestion": {
    "AzureOpenAI": {
      "EmbeddingDeployment": "text-embedding-3-small"
    },
    "AzureSearch": {
      "IndexName": "semantichub-kb"
    },
    "Chunking": {
      "TargetTokens": 500,
      "MaxTokens": 1000,
      "OverlapTokens": 100
    }
  }
}
```

### Authentication

- **Development**: Azure CLI credentials 
- **Production**: Managed Identity / Service Principal

## Troubleshooting

### Azure RBAC 

**Symptom**: "403 Forbidden" or "Insufficient privileges"

**Solution**:
1. Run RBAC setup scripts: `./src/SemanticHub.AppHost/Scripts/setup-all-rbac.sh`
2. Wait 5-10 minutes for propagation
3. Verify role assignments in Azure Portal → Resource Group → Access Control (IAM)
4. Re-login: `az logout && az login`

### Resource Provisioning 

**Symptom**: "Resource already exists" or naming conflicts

**Solution**:
1. Edit resource names in `src/SemanticHub.AppHost/AppHost.cs`
2. Ensure names are globally unique 
3. Verify subscription has available quota for AI Search and OpenAI

## Links

- [Microsoft Agent Framework Documentation](https://learn.microsoft.com/azure/ai-services/agents/)
- [Azure AI Search Documentation](https://learn.microsoft.com/azure/search/)
- [.NET Aspire Documentation](https://learn.microsoft.com/dotnet/aspire/)
- [Azure OpenAI Service](https://learn.microsoft.com/azure/ai-services/openai/)

## License

This project is provided as-is for demonstration and educational purposes.