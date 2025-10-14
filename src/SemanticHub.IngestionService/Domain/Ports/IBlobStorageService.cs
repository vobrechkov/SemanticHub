using Azure.Storage.Blobs.Models;

namespace SemanticHub.IngestionService.Domain.Ports;

/// <summary>
/// Abstraction over blob storage operations.
/// </summary>
public interface IBlobStorageService
{
    Task<List<BlobItem>> GetBlobsAsync(
        string blobPath,
        string? containerName = null,
        CancellationToken cancellationToken = default);

    Task<string> ReadBlobContentAsync(
        string blobName,
        string? containerName = null,
        CancellationToken cancellationToken = default);

    List<BlobItem> FilterBySupportedExtensions(List<BlobItem> blobs, params string[] extensions);
}
