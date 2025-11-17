using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SemanticHub.Api.Models;
using SemanticHub.Api.Workflows;

namespace SemanticHub.Api.Endpoints;

/// <summary>
/// Test helper class to expose private endpoint methods for testing
/// Note: In production code, these would be tested via integration tests
/// This is a simplified approach for unit testing the workflow logic
/// </summary>
public static class WorkflowEndpoints_TestHelper
{
    public static async Task<IResult> ExecuteIngestionWorkflowAsync(
        WorkflowExecutionRequest request,
        KnowledgeIngestionWorkflow ingestionWorkflow,
        ILogger<KnowledgeIngestionWorkflow> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            if (request.Parameters == null || !request.Parameters.TryGetValue("document", out var documentObj))
            {
                return Results.BadRequest(new { error = "Missing 'document' parameter in request" });
            }

            var documentContent = documentObj?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(documentContent))
            {
                return Results.BadRequest(new { error = "Document content cannot be empty" });
            }

            var workflowAgent = ingestionWorkflow.CreateWorkflow();
            var result = await workflowAgent.RunAsync(documentContent, cancellationToken: cancellationToken);

            return Results.Ok(new WorkflowExecutionResponse
            {
                ExecutionId = Guid.NewGuid().ToString(),
                Status = "completed",
                Output = new Dictionary<string, object>
                {
                    ["result"] = result.ToString() ?? "Workflow completed"
                }
            });
        }
        catch (Exception ex)
        {
            return Results.Problem(title: "Workflow Error", detail: ex.Message, statusCode: 500);
        }
    }

    public static async Task<IResult> ExecuteResearchWorkflowAsync(
        WorkflowExecutionRequest request,
        ResearchWorkflow researchWorkflow,
        ILogger<ResearchWorkflow> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            if (request.Parameters == null || !request.Parameters.TryGetValue("query", out var queryObj))
            {
                return Results.BadRequest(new { error = "Missing 'query' parameter in request" });
            }

            var query = queryObj?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(query))
            {
                return Results.BadRequest(new { error = "Query cannot be empty" });
            }

            var workflowAgent = await researchWorkflow.CreateWorkflowAsync();
            var result = await workflowAgent.RunAsync(query);

            return Results.Ok(new WorkflowExecutionResponse
            {
                ExecutionId = Guid.NewGuid().ToString(),
                Status = "completed",
                Output = new Dictionary<string, object>
                {
                    ["result"] = result.ToString() ?? "Research completed"
                }
            });
        }
        catch (Exception ex)
        {
            return Results.Problem(title: "Workflow Error", detail: ex.Message, statusCode: 500);
        }
    }

    public static async Task<IResult> ExecuteFastResearchWorkflowAsync(
        WorkflowExecutionRequest request,
        ResearchWorkflow researchWorkflow,
        ILogger<ResearchWorkflow> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            if (request.Parameters == null || !request.Parameters.TryGetValue("query", out var queryObj))
            {
                return Results.BadRequest(new { error = "Missing 'query' parameter in request" });
            }

            var query = queryObj?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(query))
            {
                return Results.BadRequest(new { error = "Query cannot be empty" });
            }

            var workflowAgent = await researchWorkflow.CreateFastResearchWorkflowAsync();
            var result = await workflowAgent.RunAsync(query);

            return Results.Ok(new WorkflowExecutionResponse
            {
                ExecutionId = Guid.NewGuid().ToString(),
                Status = "completed",
                Output = new Dictionary<string, object>
                {
                    ["result"] = result.ToString() ?? "Fast research completed"
                }
            });
        }
        catch (Exception ex)
        {
            return Results.Problem(title: "Workflow Error", detail: ex.Message, statusCode: 500);
        }
    }
}
