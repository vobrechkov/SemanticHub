using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using SemanticHub.IngestionService.Configuration;
using SemanticHub.IngestionService.Domain.Ports;
using SemanticHub.IngestionService.Domain.Sitemaps;
using SemanticHub.IngestionService.Diagnostics;

namespace SemanticHub.IngestionService.Services.Sitemaps;

/// <summary>
/// Retrieves sitemap documents over HTTP with gzip handling and safety checks.
/// </summary>
public sealed class HttpSitemapFetcher(
    HttpClient httpClient,
    ILogger<HttpSitemapFetcher> logger,
    IngestionOptions options) : ISitemapFetcher
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ILogger<HttpSitemapFetcher> _logger = logger;
    private readonly SitemapIngestionOptions _sitemapOptions = options.Sitemap;

    public async Task<SitemapFetchResult> FetchAsync(
        Uri sitemapUri,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sitemapUri);

        using var activity = IngestionTelemetry.ActivitySource.StartActivity("Sitemap.Fetch");
        activity?.SetTag("ingestion.sitemap.url", sitemapUri.AbsoluteUri);

        var stopwatch = Stopwatch.StartNew();
        var tagList = new TagList
        {
            { "host", sitemapUri.Host }
        };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, sitemapUri);
            request.Headers.UserAgent.ParseAdd(_sitemapOptions.UserAgent);
            request.Headers.Accept.TryParseAdd("application/xml, text/xml;q=0.9, text/plain;q=0.5");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(_sitemapOptions.FetchTimeoutSeconds));

            var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                activity?.SetStatus(ActivityStatusCode.Error, $"HTTP {response.StatusCode}");
                tagList.Add("status", "http_error");
                IngestionTelemetry.SitemapsFetched.Add(1, tagList);

                _logger.LogWarning(
                    "Failed to fetch sitemap {Url}. Status code {Status}",
                    sitemapUri,
                    response.StatusCode);

                return SitemapFetchResult.FromFailure(response.StatusCode, $"Sitemap fetch failed with status {response.StatusCode}");
            }

            if (response.Content.Headers.ContentLength is long length &&
                length > _sitemapOptions.MaxSitemapBytes)
            {
                activity?.SetStatus(ActivityStatusCode.Error, "ContentTooLarge");
                tagList.Add("status", "too_large");
                IngestionTelemetry.SitemapsFetched.Add(1, tagList);

                _logger.LogWarning(
                    "Sitemap {Url} exceeded configured size limit ({Length} bytes)",
                    sitemapUri,
                    length);

                return SitemapFetchResult.FromFailure(HttpStatusCode.RequestEntityTooLarge, "Sitemap document exceeded configured size limit.");
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var content = await ReadContentAsync(sitemapUri, response.Content.Headers, responseStream, cancellationToken);

            var document = new SitemapDocument(
                sitemapUri,
                content,
                IsSitemapIndex(content),
                DateTimeOffset.UtcNow);

            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("ingestion.sitemap.durationMs", stopwatch.Elapsed.TotalMilliseconds);
            tagList.Add("status", "success");
            IngestionTelemetry.SitemapsFetched.Add(1, tagList);

            _logger.LogInformation(
                "Fetched sitemap {Url} ({Length} chars) in {Elapsed} ms",
                sitemapUri,
                content.Length,
                stopwatch.Elapsed.TotalMilliseconds);

            return SitemapFetchResult.FromSuccess(document);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, "Timeout");
            tagList.Add("status", "timeout");
            IngestionTelemetry.SitemapsFetched.Add(1, tagList);

            _logger.LogWarning(ex, "Timed out fetching sitemap {Url}", sitemapUri);
            return SitemapFetchResult.FromFailure(HttpStatusCode.RequestTimeout, "Sitemap fetch timed out.");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            tagList.Add("status", "exception");
            IngestionTelemetry.SitemapsFetched.Add(1, tagList);

            _logger.LogError(ex, "Unexpected error fetching sitemap {Url}", sitemapUri);
            return SitemapFetchResult.FromFailure(null, ex.Message);
        }
    }

    private async Task<string> ReadContentAsync(
        Uri sitemapUri,
        HttpContentHeaders headers,
        Stream stream,
        CancellationToken cancellationToken)
    {
        var isGzip = headers.ContentEncoding.Any(encoding =>
            encoding.Contains("gzip", StringComparison.OrdinalIgnoreCase))
            || sitemapUri.AbsolutePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase);

        Stream effectiveStream = stream;
        if (isGzip)
        {
            effectiveStream = new GZipStream(stream, CompressionMode.Decompress, leaveOpen: true);
        }

        using var reader = new StreamReader(
            effectiveStream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 1024,
            leaveOpen: true);

        var content = await reader.ReadToEndAsync(cancellationToken);

        if (isGzip)
        {
            await effectiveStream.DisposeAsync();
        }

        return content;
    }

    private static bool IsSitemapIndex(string content) =>
        content.Contains("<sitemapindex", StringComparison.OrdinalIgnoreCase);
}
