# Sitemap Ingestion Workflow

The sitemap ingestion workflow traverses XML sitemap and sitemap index documents, scrapes each page, and pushes the resulting content into Azure AI Search. It builds on the HTML/Markdown processors introduced in Phase 1 and adds crawl-specific resiliency, heuristics, and instrumentation.

## Endpoint

```
POST /ingestion/sitemap
```

### Request Body

```
{
  "sitemapUrl": "https://contoso.com/sitemap.xml",
  "documentIdPrefix": "contoso",
  "tags": ["docs", "public"],
  "metadata": { "source": "marketing" },
  "allowedHosts": ["contoso.com"],
  "maxPages": 250,
  "maxDepth": 2,
  "throttleMilliseconds": 200,
  "respectRobotsTxt": true
}
```

### Response

```
{
  "success": true,
  "sitemapUrl": "https://contoso.com/sitemap.xml",
  "totalDiscovered": 120,
  "totalFiltered": 10,
  "totalIngested": 110,
  "totalFailed": 0,
  "durationMs": 28450,
  "message": "Ingested 110 of 110 sitemap URLs.",
  "errors": null
}
```

## Configuration

The `Ingestion:Sitemap` section in `appsettings.json` controls default behaviour:

```json
"Sitemap": {
  "MaxPages": 200,
  "MaxDepth": 2,
  "MaxConcurrency": 3,
  "ThrottleMilliseconds": 250,
  "RespectRobotsTxt": true,
  "UserAgent": "SemanticHubIngestionBot/1.0",
  "FetchTimeoutSeconds": 30,
  "MaxSitemapBytes": 2000000,
  "RecencyHalfLifeDays": 30,
  "ChangeFrequencyWeight": 0.75
}
```

## Components

- `HttpSitemapFetcher` downloads sitemap documents with retry/backoff, gzip support, and size limits.
- `XmlSitemapParser` normalises namespaces and produces `SitemapEntry` value objects.
- `SitemapUrlFilterPolicy` applies host whitelists and robots.txt rules (cached per host).
- `DefaultChangeFrequencyHeuristic` prioritises pages using `<changefreq>`, `<priority>`, and last-modified hints.
- `SiteMapIngestionWorkflow` orchestrates sitemap traversal, throttled scraping, HTML/Markdown ingestion, and telemetry emission.

## Telemetry

New counters and histograms track the sitemap crawl:

- `sitemaps_fetched_total`
- `sitemap_urls_discovered_total`
- `sitemap_urls_ingested_total`
- `sitemap_url_failures_total`

Activity spans (`Workflow.SitemapIngestion`, `Sitemap.Fetch`, `Sitemap.ProcessUrl`) capture per-document diagnostics for tracing.
