# Azure Blob Storage Ingestion

This document describes the new blob storage ingestion capability added to the SemanticHub ingestion service.

## Overview

The blob ingestion endpoint allows you to ingest documents directly from Azure Blob Storage. It automatically detects file types and routes them to the appropriate ingestion mechanism:

- **Markdown files** (`.md`, `.markdown`) → Markdown ingestion
- **OpenAPI specs** (`.yml`, `.yaml`, `.json`) → OpenAPI ingestion (with validation)
- **HTML files** (`.html`, `.htm`) → HTML ingestion

## Features

- **Asynchronous Processing**: Returns immediately with `202 Accepted` status
- **Parallel Processing**: Processes different file types concurrently
- **Automatic File Type Detection**: Routes files based on extension
- **OpenAPI Validation**: Verifies that YAML/JSON files are actually OpenAPI specs
- **Configurable Container**: Supports custom container names or uses default

## Configuration

Add the following to your `appsettings.json`:

```json
{
  "Ingestion": {
    "BlobStorage": {
      "Endpoint": "https://your-storage-account.blob.core.windows.net",
      "DefaultContainer": "documents",
      "ConnectionString": ""
    }
  }
}
```

> **Note**: When using Aspire service discovery, the endpoint and connection details are automatically configured from the `blobs` resource reference.

## API Endpoints

### 1. Ingest from Blob Storage

**POST** `/ingestion/blob`

Reads files from a blob path and ingests them based on file type.

**Request Body:**
```json
{
  "blobPath": "docs/",
  "containerName": "documents",
  "tags": ["documentation", "blob-ingestion"],
  "metadata": {
    "source": "blob-storage",
    "ingestedBy": "user-id"
  }
}
```

**Response:** `202 Accepted`
```json
{
  "status": "Accepted",
  "message": "Blob ingestion started for path: docs/. Processing will continue in the background.",
  "blobPath": "docs/"
}
```

**Parameters:**
- `blobPath` (required): Blob path or prefix (e.g., `"folder/"` or `"folder/file.md"`)
- `containerName` (optional): Container name (defaults to configured `DefaultContainer`)
- `tags` (optional): Tags to apply to all ingested documents
- `metadata` (optional): Metadata to attach to all ingested documents

### 2. Ingest HTML Content

**POST** `/ingestion/html`

Converts HTML content to Markdown and ingests it.

**Request Body:**
```json
{
  "title": "My HTML Document",
  "sourceUrl": "https://example.com/page.html",
  "tags": ["html", "documentation"],
  "content": "<!DOCTYPE html><html><head><title>Test</title></head><body><h1>Content</h1></body></html>"
}
```

**Response:** `200 OK`
```json
{
  "success": true,
  "documentId": "my-html-document",
  "indexName": "knowledge-index",
  "chunksIndexed": 2,
  "message": "Document 'my-html-document' ingested into index 'knowledge-index'."
}
```

## Implementation Details

### Service Architecture

1. **BlobStorageService**: Handles Azure Blob Storage operations
   - Lists blobs matching a path prefix
   - Downloads blob content
   - Filters by supported file extensions

2. **DocumentIngestionService**: Extended with new methods
   - `IngestFromBlobAsync`: Orchestrates blob-based ingestion
   - `IngestHtmlAsync`: Converts and ingests HTML content
   - `ProcessMarkdownFilesAsync`: Batch processes Markdown files
   - `ProcessOpenApiFilesAsync`: Validates and processes API specs
   - `ProcessHtmlFilesAsync`: Batch processes HTML files

3. **OpenAPI Validation**: Basic content-based verification
   ```csharp
   private static bool IsOpenApiSpec(string content)
   {
       return content.Contains("openapi:", StringComparison.OrdinalIgnoreCase) ||
              content.Contains("swagger:", StringComparison.OrdinalIgnoreCase) ||
              (content.Contains("\"openapi\"", StringComparison.OrdinalIgnoreCase) &&
               content.Contains("\"info\"", StringComparison.OrdinalIgnoreCase)) ||
              (content.Contains("\"swagger\"", StringComparison.OrdinalIgnoreCase) &&
               content.Contains("\"info\"", StringComparison.OrdinalIgnoreCase));
   }
   ```

### Parallel Processing Strategy

Files are grouped by type and processed in parallel:

```csharp
var tasks = new List<Task>();

if (markdownFiles.Count > 0)
    tasks.Add(ProcessMarkdownFilesAsync(markdownFiles, request, result, cancellationToken));

if (yamlFiles.Count > 0 || jsonFiles.Count > 0)
{
    var openApiFiles = yamlFiles.Concat(jsonFiles).ToList();
    tasks.Add(ProcessOpenApiFilesAsync(openApiFiles, request, result, cancellationToken));
}

if (htmlFiles.Count > 0)
    tasks.Add(ProcessHtmlFilesAsync(htmlFiles, request, result, cancellationToken));

await Task.WhenAll(tasks);
```

### Error Handling

- Individual file failures don't stop the overall process
- Errors are collected and returned in the result
- Thread-safe error collection using `lock` statements

## Aspire Integration

The ingestion service now references the `blobs` resource from AppHost:

```csharp
ingestion = builder.AddProject<Projects.SemanticHub_IngestionService>("ingestion")
    .WithReference(openai).WaitFor(openai)
    .WithReference(search).WaitFor(search)
    .WithReference(blobs).WaitFor(storage)  // ← New reference
    .WithEnvironment("Ingestion__AzureOpenAI__EmbeddingDeployment", embeddingDeployment.Resource.Name);
```

## Testing

Use the provided HTTP file for testing:

**File:** `src/SemanticHub.IngestionService/test-blob-ingestion.http`

```http
### Test Blob Ingestion - Ingest all supported files from a blob path
POST {{ingestionUrl}}/ingestion/blob
Content-Type: application/json

{
  "blobPath": "docs/",
  "containerName": "documents",
  "tags": ["documentation", "blob-ingestion"]
}
```

## Future Enhancements

The following features are planned for future releases:

1. **Status Monitoring**: Track ingestion progress and query status
2. **Webhooks**: Notify external systems when ingestion completes
3. **Retry Logic**: Automatic retry for failed files
4. **Batch Size Limits**: Control concurrent file processing
5. **File Size Limits**: Skip or handle very large files differently
6. **Custom File Handlers**: Plugin architecture for additional file types

## Related Files

- `Application/Workflows/BulkMarkdownIngestionWorkflow.cs` - Blob ingestion orchestration
- `Services/BlobStorageService.cs` - Blob storage operations
- `Application/Workflows/HtmlIngestionWorkflow.cs` - HTML ingestion coordination
- `Endpoints/IngestionEndpoints.cs` - Updated endpoint mappings
- `Models/MarkdownIngestionRequest.cs` - Request models (BlobIngestionRequest, HtmlIngestionRequest)
- `Configuration/IngestionOptions.cs` - Configuration options (AzureBlobStorageOptions)
- `test-blob-ingestion.http` - HTTP test file
