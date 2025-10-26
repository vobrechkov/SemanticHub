using System.Diagnostics;
using SemanticHub.IngestionService.Configuration;
using SemanticHub.IngestionService.Diagnostics;
using SemanticHub.IngestionService.Domain.Aggregates;
using SemanticHub.IngestionService.Domain.OpenApi;
using SemanticHub.IngestionService.Domain.Ports;

namespace SemanticHub.IngestionService.Services.OpenApi;

/// <summary>
/// Fetches OpenAPI specification content from HTTP, local filesystem, or Azure Blob Storage sources.
/// </summary>
public sealed class OpenApiSpecLocator(
    HttpClient httpClient,
    IBlobStorageService blobStorageService,
    IngestionOptions options,
    ILogger<OpenApiSpecLocator> logger) : IOpenApiSpecLocator
{
    private const string BlobScheme = "blob";
    private const string AzureBlobScheme = "azure-blob";

    public async Task<OpenApiSpecDocument> LocateAsync(
        OpenApiSpecificationIngestion request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var activity = IngestionTelemetry.ActivitySource.StartActivity("OpenApiSpecLocator.Resolve");
        activity?.SetTag("ingestion.openapi.specSource", request.SpecSource);

        try
        {
            if (request.Resource.SourceUri is not null)
            {
                return await ResolveFromUriAsync(request.Resource.SourceUri, request, activity, cancellationToken);
            }

            return await ResolveFromSourceAsync(request.SpecSource, activity, cancellationToken);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogError(ex, "Failed to resolve OpenAPI specification from {Source}", request.SpecSource);
            throw;
        }
    }

    private async Task<OpenApiSpecDocument> ResolveFromUriAsync(
        Uri sourceUri,
        OpenApiSpecificationIngestion request,
        Activity? activity,
        CancellationToken cancellationToken)
    {
        if (string.Equals(sourceUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sourceUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("Downloading OpenAPI specification from {Uri}", sourceUri);
            var content = await httpClient.GetStringAsync(sourceUri, cancellationToken);
            activity?.SetTag("ingestion.openapi.specTransport", "http");
            return new OpenApiSpecDocument(sourceUri.ToString(), content, sourceUri);
        }

        if (sourceUri.Scheme == Uri.UriSchemeFile)
        {
            var path = sourceUri.LocalPath;
            logger.LogInformation("Reading OpenAPI specification from file {Path}", path);
            var content = await File.ReadAllTextAsync(path, cancellationToken);
            activity?.SetTag("ingestion.openapi.specTransport", "file");
            return new OpenApiSpecDocument(path, content, sourceUri);
        }

        if (IsBlobScheme(sourceUri.Scheme))
        {
            var (container, blobName) = ParseBlobReference(sourceUri);
            var content = await ReadBlobAsync(container, blobName, cancellationToken);
            activity?.SetTag("ingestion.openapi.specTransport", "blob");
            return new OpenApiSpecDocument(sourceUri.ToString(), content, sourceUri);
        }

        logger.LogDebug("Unhandled URI scheme {Scheme}, falling back to raw source string.", sourceUri.Scheme);
        return await ResolveFromSourceAsync(request.SpecSource, activity, cancellationToken);
    }

    private async Task<OpenApiSpecDocument> ResolveFromSourceAsync(
        string specSource,
        Activity? activity,
        CancellationToken cancellationToken)
    {
        var trimmed = specSource.Trim();

        if (File.Exists(trimmed))
        {
            var absolutePath = Path.GetFullPath(trimmed);
            logger.LogInformation("Reading OpenAPI specification from file {Path}", absolutePath);
            var fileContent = await File.ReadAllTextAsync(absolutePath, cancellationToken);
            activity?.SetTag("ingestion.openapi.specTransport", "file");
            return new OpenApiSpecDocument(absolutePath, fileContent, new Uri(absolutePath));
        }

        if (TryParseBlobReference(trimmed, out var container, out var blobName))
        {
            var content = await ReadBlobAsync(container, blobName, cancellationToken);
            var blobUri = BuildBlobUri(container, blobName);
            activity?.SetTag("ingestion.openapi.specTransport", "blob");
            return new OpenApiSpecDocument(trimmed, content, blobUri);
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri) &&
            (string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            logger.LogInformation("Downloading OpenAPI specification from {Uri}", absoluteUri);
            var content = await httpClient.GetStringAsync(absoluteUri, cancellationToken);
            activity?.SetTag("ingestion.openapi.specTransport", "http");
            return new OpenApiSpecDocument(absoluteUri.ToString(), content, absoluteUri);
        }

        throw new FileNotFoundException($"Unable to resolve OpenAPI specification from '{specSource}'. File does not exist and the source is not a recognized URI.");
    }

    private static bool IsBlobScheme(string scheme) =>
        string.Equals(scheme, BlobScheme, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(scheme, AzureBlobScheme, StringComparison.OrdinalIgnoreCase);

    private static (string? Container, string BlobName) ParseBlobReference(Uri sourceUri)
    {
        var absolutePath = sourceUri.AbsolutePath.TrimStart('/');
        var container = sourceUri.Host;

        if (string.IsNullOrEmpty(container))
        {
            var separatorIndex = absolutePath.IndexOf('/', StringComparison.Ordinal);
            if (separatorIndex > 0)
            {
                container = absolutePath[..separatorIndex];
                absolutePath = absolutePath[(separatorIndex + 1)..];
            }
        }

        return (string.IsNullOrWhiteSpace(container) ? null : container, absolutePath);
    }

    private static bool TryParseBlobReference(
        string specSource,
        out string? container,
        out string blobName)
    {
        container = null;
        blobName = specSource;

        if (!specSource.Contains("://", StringComparison.Ordinal))
        {
            var parts = specSource.Split(':', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                container = parts[0];
                blobName = parts[1];
                return true;
            }
        }

        if (Uri.TryCreate(specSource, UriKind.Absolute, out var uri) && IsBlobScheme(uri.Scheme))
        {
            var parsed = ParseBlobReference(uri);
            container = parsed.Container;
            blobName = parsed.BlobName;
            return true;
        }

        return false;
    }

    private async Task<string> ReadBlobAsync(
        string? container,
        string blobName,
        CancellationToken cancellationToken)
    {
        var resolvedContainer = container ?? options.BlobStorage.DefaultContainer;
        logger.LogInformation(
            "Reading OpenAPI specification from blob storage. Container: {Container}, Blob: {Blob}",
            resolvedContainer ?? "<default>",
            blobName);

        return await blobStorageService.ReadBlobContentAsync(
            blobName,
            resolvedContainer,
            cancellationToken);
    }

    private static Uri BuildBlobUri(string? container, string blobName)
    {
        var safeContainer = string.IsNullOrWhiteSpace(container) ? "-" : container;
        return new Uri($"{BlobScheme}://{safeContainer}/{blobName}", UriKind.Absolute);
    }
}
