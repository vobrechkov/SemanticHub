namespace SemanticHub.IngestionService.Models;

/// <summary>
/// Represents an ingestion job with status tracking
/// </summary>
public class IngestionJob
{
    /// <summary>
    /// Unique job identifier
    /// </summary>
    public required string JobId { get; set; }

    /// <summary>
    /// Job type (WebPage, OpenAPI, etc.)
    /// </summary>
    public required string JobType { get; set; }

    /// <summary>
    /// Source URL or identifier
    /// </summary>
    public required string Source { get; set; }

    /// <summary>
    /// Current job status
    /// </summary>
    public IngestionStatus Status { get; set; } = IngestionStatus.Pending;

    /// <summary>
    /// When the job was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the job started processing
    /// </summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>
    /// When the job completed
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Number of documents processed
    /// </summary>
    public int DocumentsProcessed { get; set; }

    /// <summary>
    /// Number of chunks created
    /// </summary>
    public int ChunksCreated { get; set; }

    /// <summary>
    /// Error message if job failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Additional job configuration
    /// </summary>
    public Dictionary<string, object> Configuration { get; set; } = new();

    /// <summary>
    /// Progress messages and logs
    /// </summary>
    public List<string> Logs { get; set; } = new();
}

/// <summary>
/// Ingestion job status
/// </summary>
public enum IngestionStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}
