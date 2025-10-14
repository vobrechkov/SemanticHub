using SemanticHub.IngestionService.Domain.Results;

namespace SemanticHub.IngestionService.Domain.Workflows;

/// <summary>
/// Contract for ingestion workflows that orchestrate scraping, processing, and indexing.
/// </summary>
public interface IIngestionWorkflow<in TRequest> : IIngestionWorkflow<TRequest, IngestionOutcome>
{
}

public interface IIngestionWorkflow<in TRequest, TResponse>
{
    Task<TResponse> ExecuteAsync(TRequest request, CancellationToken cancellationToken = default);
}
