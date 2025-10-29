# Batch Web Page Ingestion Workflow

The batch web page ingestion workflow allows concurrent ingestion of multiple URLs with automatic title inference. Unlike the single-page ingestion endpoint (which allows title overrides), this workflow is optimized for bulk URL processing similar to sitemap ingestion, but with explicit URL lists.

## Endpoint

```
POST /ingestion/batch-webpage
```

### Request Body

```json
{
  "urls": [
    "https://example.com/page1",
    "https://example.com/page2",
    "https://example.com/page3"
  ],
  "maxConcurrency": 3,
  "throttleMilliseconds": 250,
  "tags": ["documentation", "public"],
  "metadata": {
    "source": "manual-batch",
    "category": "technical-docs"
  }
}
```

### Response

```json
{
  "success": true,
  "totalRequested": 3,
  "totalSucceeded": 3,
  "totalFailed": 0,
  "durationMs": 2450,
  "message": "Ingested 3 of 3 web pages.",
  "results": [
    {
      "url": "https://example.com/page1",
      "success": true,
      "title": "Page 1 Title",
      "chunksIndexed": 5,
      "errorMessage": null
    },
    {
      "url": "https://example.com/page2",
      "success": true,
      "title": "Page 2 Title",
      "chunksIndexed": 8,
      "errorMessage": null
    },
    {
      "url": "https://example.com/page3",
      "success": true,
      "title": "Page 3 Title",
      "chunksIndexed": 3,
      "errorMessage": null
    }
  ],
  "errors": []
}
```

## Features

### Automatic Title Inference
Unlike the single-page ingestion endpoint (`/ingestion/webpage`), batch ingestion always infers titles from page content (typically from the `<title>` tag or first `<h1>` element). This ensures consistent behavior across all pages in the batch.

### Concurrency Control
The `maxConcurrency` parameter limits the number of simultaneous scraping/ingestion operations. This prevents overwhelming target servers and respects rate limits.

**Default:** `3` concurrent requests  
**Recommended:** 2-5 for public websites, higher for internal APIs

### Throttling
The `throttleMilliseconds` parameter adds a delay between requests to further reduce server load and avoid triggering rate limiting.

**Default:** `250ms` between requests  
**Recommended:** 100-500ms for most sites, increase if experiencing 429 errors

### Partial Failure Handling
The workflow continues processing all URLs even if some fail. Failed pages are recorded in the response with error details, while successful pages are indexed normally.

**Response behavior:**
- `success: false` if ANY page failed
- `success: true` only if ALL pages succeeded
- Individual outcomes in `results` array
- Failed URLs collected in `errors` array

## Architecture

### Workflow Pipeline

```
BatchWebPageIngestionRequest (DTO)
    ↓ (mapper)
BatchWebPageIngestion (Domain Aggregate)
    ↓ (workflow)
[For Each URL in Parallel with Semaphore]
    ↓
IHtmlScraper.ScrapeAsync()
    ↓
IHtmlProcessor.IngestWebPageAsync()
    ↓
PageIngestionOutcome
    ↓
BatchWebPageIngestionResult
```

### Key Classes

**Domain Layer:**
- `BatchWebPageIngestion` - Aggregate root with URLs, concurrency settings, and metadata
- `BatchWebPageIngestionResult` - Summary outcome with individual page results
- `PageIngestionOutcome` - Per-URL result with title, chunks indexed, and errors

**Application Layer:**
- `BatchWebPageIngestionWorkflow` - Orchestrates concurrent scraping and ingestion
- Uses `SemaphoreSlim` to enforce concurrency limits
- Continues on errors to maximize throughput

**Models (DTO):**
- `BatchWebPageIngestionRequest` - HTTP request payload
- `BatchWebPageIngestionRequest.ToDomain()` mapper

## Comparison with Other Endpoints

### Single Web Page (`/ingestion/webpage`)
- **Use case:** Single URL with optional title override
- **Title:** Can be explicitly provided or inferred
- **Concurrency:** N/A (single page)
- **Response:** Single `DocumentIngestionResult`

### Batch Web Page (`/ingestion/batch-webpage`)
- **Use case:** Multiple known URLs from any source
- **Title:** Always inferred from content
- **Concurrency:** Configurable with semaphore
- **Response:** `BatchWebPageIngestionResult` with per-URL outcomes

### Sitemap (`/ingestion/sitemap`)
- **Use case:** Discover and ingest URLs from sitemap.xml
- **Title:** Always inferred from content
- **Concurrency:** Configurable + sitemap-specific features (robots.txt, change frequency, depth limits)
- **Response:** `SitemapIngestionResult` with discovery metrics

## Configuration

Batch ingestion inherits throttling defaults from the configuration but allows per-request overrides:

```json
{
  "Ingestion": {
    "Sitemap": {
      "ThrottleMilliseconds": 250,
      "MaxConcurrency": 3
    }
  }
}
```

These values are used as defaults when not specified in the request.

## Usage Examples

### Basic Batch Ingestion
```bash
curl -X POST https://localhost:7123/ingestion/batch-webpage \
  -H "Content-Type: application/json" \
  -d '{
    "urls": [
      "https://docs.microsoft.com/dotnet",
      "https://docs.microsoft.com/azure"
    ]
  }'
```

### High-Throughput Batch
```bash
curl -X POST https://localhost:7123/ingestion/batch-webpage \
  -H "Content-Type: application/json" \
  -d '{
    "urls": ["https://api.example.com/docs/v1", "https://api.example.com/docs/v2"],
    "maxConcurrency": 5,
    "throttleMilliseconds": 100,
    "tags": ["api-docs", "v1", "v2"]
  }'
```

### Cautious Batch (Rate-Limited Sites)
```bash
curl -X POST https://localhost:7123/ingestion/batch-webpage \
  -H "Content-Type: application/json" \
  -d '{
    "urls": ["https://slow-site.com/page1", "https://slow-site.com/page2"],
    "maxConcurrency": 1,
    "throttleMilliseconds": 1000
  }'
```

## Testing

Comprehensive unit tests cover:
- Successful multi-URL ingestion
- Partial failure scenarios
- Processing exceptions
- Concurrency limit enforcement
- Tag and metadata propagation
- Null request handling

**Run tests:**
```bash
dotnet test --filter "FullyQualifiedName~BatchWebPageIngestionWorkflowTests"
```

## Performance Considerations

### Concurrency vs. Latency
- **Higher concurrency** = faster batch completion, higher server load
- **Lower concurrency** = slower batch completion, more respectful of target servers

### Throttling Strategy
- Use throttling for public websites to avoid IP bans
- Reduce/eliminate throttling for internal services
- Monitor HTTP 429 (Too Many Requests) responses and adjust accordingly

### Batch Size Recommendations
- **Small batches (1-10 URLs):** Low overhead, use batch endpoint for consistency
- **Medium batches (10-100 URLs):** Sweet spot for the batch endpoint
- **Large batches (100+ URLs):** Consider sitemap endpoint or chunking into multiple requests

## Error Handling

### Scrape Failures (4xx/5xx Status Codes)
- Recorded as failed in results
- Error message includes status code
- Processing continues for remaining URLs

### Processing Exceptions
- Caught and recorded per-URL
- Exception message included in `PageIngestionOutcome.ErrorMessage`
- Workflow continues (fire-and-forget per-URL semantics)

### Network Timeouts
- Handled by `IHtmlScraper` implementation (typically Playwright)
- Configurable timeout in scraper settings
- Failed scrapes treated as partial failures

## Observability

### OpenTelemetry Spans
- `Workflow.BatchWebPageIngestion` - Top-level workflow span
  - Tags: `ingestion.workflow`, `ingestion.batch.urlCount`, `ingestion.batch.maxConcurrency`
  - Tags: `ingestion.batch.succeeded`, `ingestion.batch.failed`, `ingestion.workflow.durationMs`

### Metrics
- `ingestion.webpages.scraped` - Counter with tags: `status` (success/failed), `sourceType` (batch-webpage), `host`

### Structured Logging
```
Starting batch web page ingestion for {Count} URLs with max concurrency {MaxConcurrency}
Batch web page ingestion completed. Succeeded: {Succeeded}/{Total}, Failed: {Failed}
Failed to scrape {Url}. Status: {Status}
Error ingesting page {Url}
```

## Future Enhancements

- **Retry logic:** Automatic retry for transient failures (503, timeouts)
- **Priority queuing:** Prioritize URLs based on heuristics
- **Streaming responses:** Server-sent events for real-time progress
- **Resume support:** Continue interrupted batches from checkpoint
- **Deduplication:** Skip already-indexed URLs based on document ID or URL hash
