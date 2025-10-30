using SemanticHub.Api.Models;
using SemanticHub.Api.Workflows;

namespace SemanticHub.Api.Endpoints;

/// <summary>
/// API endpoints for multi-agent workflow execution
/// </summary>
public static class WorkflowEndpoints
{
    /// <summary>
    /// Maps workflow-related endpoints
    /// </summary>
    public static IEndpointRouteBuilder MapWorkflowEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/workflows")
            .WithTags("Workflows");

        group.MapPost("/ingest", ExecuteIngestionWorkflowAsync)
            .WithName("IngestionWorkflow")
            .WithSummary("Execute document ingestion workflow")
            .WithDescription("Runs a multi-agent workflow to validate, extract, index, and verify a document in the knowledge base.");

        group.MapPost("/research", ExecuteResearchWorkflowAsync)
            .WithName("ResearchWorkflow")
            .WithSummary("Execute research workflow")
            .WithDescription("Runs a multi-agent workflow to search, analyze, and synthesize information from the knowledge base.");

        group.MapPost("/research/fast", ExecuteFastResearchWorkflowAsync)
            .WithName("FastResearchWorkflow")
            .WithSummary("Execute fast research workflow")
            .WithDescription("Runs a simplified 2-step research workflow for quick answers.");

        group.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "workflows" }))
            .WithName("WorkflowHealth")
            .WithSummary("Check workflow service health");

        return endpoints;
    }

    /// <summary>
    /// Executes the document ingestion workflow
    /// </summary>
    private static async Task<IResult> ExecuteIngestionWorkflowAsync(
        WorkflowExecutionRequest request,
        KnowledgeIngestionWorkflow ingestionWorkflow,
        ILogger<KnowledgeIngestionWorkflow> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            const string workflowName = "knowledge-ingestion";
            logger.LogInformation("Starting ingestion workflow '{WorkflowName}'", workflowName);

            // Get document content from parameters
            if (request.Parameters == null || !request.Parameters.TryGetValue("document", out var documentObj))
            {
                return Results.BadRequest(new { error = "Missing 'document' parameter in request" });
            }

            var documentContent = documentObj?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(documentContent))
            {
                return Results.BadRequest(new { error = "Document content cannot be empty" });
            }

            // Create workflow agent
            var workflowAgent = await ingestionWorkflow.CreateWorkflowAsync();

            logger.LogInformation("Executing ingestion workflow with {Length} characters of content", documentContent.Length);

            // Execute workflow
            var result = await workflowAgent.RunAsync(documentContent, cancellationToken: cancellationToken);

            var executionId = Guid.NewGuid().ToString();
            var responseMessage = result.ToString() ?? "Workflow completed";

            logger.LogInformation("Ingestion workflow '{WorkflowName}' completed successfully. Execution ID: {ExecutionId}", workflowName, executionId);

            return Results.Ok(new WorkflowExecutionResponse
            {
                ExecutionId = executionId,
                Status = "completed",
                Output = new Dictionary<string, object>
                {
                    ["result"] = responseMessage,
                    ["userId"] = request.UserId ?? "anonymous",
                    ["timestamp"] = DateTime.UtcNow,
                    ["workflowType"] = "ingestion"
                }
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing ingestion workflow");
            return Results.Problem(
                title: "Workflow Error",
                detail: ex.Message,
                statusCode: 500);
        }
    }

    /// <summary>
    /// Executes the research workflow (3-step: Search → Analyze → Synthesize)
    /// </summary>
    private static async Task<IResult> ExecuteResearchWorkflowAsync(
        WorkflowExecutionRequest request,
        ResearchWorkflow researchWorkflow,
        ILogger<ResearchWorkflow> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            const string workflowName = "research";
            logger.LogInformation("Starting research workflow '{WorkflowName}'", workflowName);

            // Get research query from parameters
            if (request.Parameters == null || !request.Parameters.TryGetValue("query", out var queryObj))
            {
                return Results.BadRequest(new { error = "Missing 'query' parameter in request" });
            }

            var query = queryObj?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(query))
            {
                return Results.BadRequest(new { error = "Query cannot be empty" });
            }

            // Create workflow agent
            var workflowAgent = await researchWorkflow.CreateWorkflowAsync();

            logger.LogInformation("Executing research workflow for query: {Query}", query);

            // Execute workflow
            var result = await workflowAgent.RunAsync(query);

            var executionId = Guid.NewGuid().ToString();
            var responseMessage = result.ToString() ?? "Research completed";

            logger.LogInformation("Research workflow '{WorkflowName}' completed successfully. Execution ID: {ExecutionId}", workflowName, executionId);

            return Results.Ok(new WorkflowExecutionResponse
            {
                ExecutionId = executionId,
                Status = "completed",
                Output = new Dictionary<string, object>
                {
                    ["result"] = responseMessage,
                    ["query"] = query,
                    ["userId"] = request.UserId ?? "anonymous",
                    ["timestamp"] = DateTime.UtcNow,
                    ["workflowType"] = "research",
                    ["steps"] = 3
                }
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing research workflow");
            return Results.Problem(
                title: "Workflow Error",
                detail: ex.Message,
                statusCode: 500);
        }
    }

    /// <summary>
    /// Executes the fast research workflow (2-step: Search → Synthesize)
    /// </summary>
    private static async Task<IResult> ExecuteFastResearchWorkflowAsync(
        WorkflowExecutionRequest request,
        ResearchWorkflow researchWorkflow,
        ILogger<ResearchWorkflow> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            const string workflowName = "fast-research";
            logger.LogInformation("Starting fast research workflow '{WorkflowName}'", workflowName);

            // Get research query from parameters
            if (request.Parameters == null || !request.Parameters.TryGetValue("query", out var queryObj))
            {
                return Results.BadRequest(new { error = "Missing 'query' parameter in request" });
            }

            var query = queryObj?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(query))
            {
                return Results.BadRequest(new { error = "Query cannot be empty" });
            }

            // Create fast workflow agent
            var workflowAgent = await researchWorkflow.CreateFastResearchWorkflowAsync();

            logger.LogInformation("Executing fast research workflow for query: {Query}", query);

            // Execute workflow
            var result = await workflowAgent.RunAsync(query);

            var executionId = Guid.NewGuid().ToString();
            var responseMessage = result.ToString() ?? "Fast research completed";

            logger.LogInformation("Fast research workflow '{WorkflowName}' completed successfully. Execution ID: {ExecutionId}", workflowName, executionId);

            return Results.Ok(new WorkflowExecutionResponse
            {
                ExecutionId = executionId,
                Status = "completed",
                Output = new Dictionary<string, object>
                {
                    ["result"] = responseMessage,
                    ["query"] = query,
                    ["userId"] = request.UserId ?? "anonymous",
                    ["timestamp"] = DateTime.UtcNow,
                    ["workflowType"] = "fast-research",
                    ["steps"] = 2
                }
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing fast research workflow");
            return Results.Problem(
                title: "Workflow Error",
                detail: ex.Message,
                statusCode: 500);
        }
    }
}
