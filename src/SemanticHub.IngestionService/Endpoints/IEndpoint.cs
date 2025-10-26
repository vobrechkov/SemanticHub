namespace SemanticHub.IngestionService.Endpoints;

/// <summary>
/// Contract for registering minimal API endpoints.
/// </summary>
public interface IEndpoint
{
    /// <summary>
    /// Maps the endpoint routes onto the provided route builder.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    void MapEndpoint(IEndpointRouteBuilder endpoints);
}
