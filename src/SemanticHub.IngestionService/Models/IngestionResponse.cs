namespace SemanticHub.IngestionService.Models;

public class IngestionResponse
{
    public bool Success { get; set; }

    public string DocumentId { get; set; } = string.Empty;

    public string? IndexName { get; set; }

    public int ChunksIndexed { get; set; }

    public string? Message { get; set; }

    public string? ErrorMessage { get; set; }
}
