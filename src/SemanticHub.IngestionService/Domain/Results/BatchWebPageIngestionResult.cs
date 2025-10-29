namespace SemanticHub.IngestionService.Domain.Results;

/// <summary>
/// Summary outcome returned from batch web page ingestion workflow executions.
/// </summary>
public sealed class BatchWebPageIngestionResult
{
    public required bool Success { get; init; }
    
    public required int TotalRequested { get; init; }
    
    public required int TotalSucceeded { get; init; }
    
    public required int TotalFailed { get; init; }
    
    public required IReadOnlyList<PageIngestionOutcome> Results { get; init; }
    
    public TimeSpan Duration { get; init; }
    
    public string? Message { get; init; }
}

/// <summary>
/// Outcome for a single page within a batch ingestion operation.
/// </summary>
public sealed class PageIngestionOutcome
{
    public required Uri Url { get; init; }
    
    public required bool Success { get; init; }
    
    public string? Title { get; init; }
    
    public int ChunksIndexed { get; init; }
    
    public string? ErrorMessage { get; init; }
}
