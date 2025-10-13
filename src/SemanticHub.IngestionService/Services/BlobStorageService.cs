using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using SemanticHub.IngestionService.Configuration;

namespace SemanticHub.IngestionService.Services;

/// <summary>
/// Service for interacting with Azure Blob Storage
/// </summary>
public class BlobStorageService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly IngestionOptions _options;
    private readonly ILogger<BlobStorageService> _logger;

    public BlobStorageService(
        BlobServiceClient blobServiceClient,
        IngestionOptions options,
        ILogger<BlobStorageService> logger)
    {
        _blobServiceClient = blobServiceClient;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Gets blobs matching the specified path and file extensions
    /// </summary>
    public async Task<List<BlobItem>> GetBlobsAsync(
        string blobPath,
        string? containerName = null,
        CancellationToken cancellationToken = default)
    {
        var container = string.IsNullOrWhiteSpace(containerName)
            ? _options.BlobStorage.DefaultContainer
            : containerName;

        _logger.LogInformation("Listing blobs in container {Container} with path {Path}", container, blobPath);

        var containerClient = _blobServiceClient.GetBlobContainerClient(container);

        // Ensure container exists
        await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var blobs = new List<BlobItem>();

        await foreach (var blob in containerClient.GetBlobsAsync(
            prefix: blobPath,
            cancellationToken: cancellationToken))
        {
            blobs.Add(blob);
        }

        _logger.LogInformation("Found {Count} blobs", blobs.Count);
        return blobs;
    }

    /// <summary>
    /// Downloads blob content as string
    /// </summary>
    public async Task<string> ReadBlobContentAsync(
        string blobName,
        string? containerName = null,
        CancellationToken cancellationToken = default)
    {
        var container = string.IsNullOrWhiteSpace(containerName)
            ? _options.BlobStorage.DefaultContainer
            : containerName;

        _logger.LogInformation("Reading blob {BlobName} from container {Container}", blobName, container);

        var containerClient = _blobServiceClient.GetBlobContainerClient(container);
        var blobClient = containerClient.GetBlobClient(blobName);

        var response = await blobClient.DownloadContentAsync(cancellationToken);
        return response.Value.Content.ToString();
    }

    /// <summary>
    /// Filters blobs by supported file extensions
    /// </summary>
    public List<BlobItem> FilterBySupportedExtensions(List<BlobItem> blobs, params string[] extensions)
    {
        var filtered = blobs
            .Where(b => extensions.Any(ext => b.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        _logger.LogInformation("Filtered {Original} blobs to {Filtered} with supported extensions", 
            blobs.Count, filtered.Count);

        return filtered;
    }
}
