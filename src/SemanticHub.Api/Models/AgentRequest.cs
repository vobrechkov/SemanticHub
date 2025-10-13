namespace SemanticHub.Api.Models;

/// <summary>
/// Request model for agent chat interactions
/// </summary>
public class AgentChatRequest
{
    /// <summary>
    /// The user's message to the agent
    /// </summary>
    public required string Message { get; set; }

    /// <summary>
    /// Optional conversation thread ID for continuing a conversation
    /// </summary>
    public string? ThreadId { get; set; }

    /// <summary>
    /// Optional user ID for personalized memory
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Optional custom instructions for this specific interaction
    /// </summary>
    public string? CustomInstructions { get; set; }
}

/// <summary>
/// Response model for agent chat interactions
/// </summary>
public class AgentChatResponse
{
    /// <summary>
    /// The agent's response message
    /// </summary>
    public required string Message { get; set; }

    /// <summary>
    /// The thread ID for this conversation
    /// </summary>
    public required string ThreadId { get; set; }

    /// <summary>
    /// Metadata about the interaction
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Request model for workflow execution
/// </summary>
public class WorkflowExecutionRequest
{
    /// <summary>
    /// The workflow ID to execute
    /// </summary>
    public required string WorkflowId { get; set; }

    /// <summary>
    /// Input parameters for the workflow
    /// </summary>
    public Dictionary<string, object>? Parameters { get; set; }

    /// <summary>
    /// Optional user ID for personalized workflow execution
    /// </summary>
    public string? UserId { get; set; }
}

/// <summary>
/// Response model for workflow execution
/// </summary>
public class WorkflowExecutionResponse
{
    /// <summary>
    /// The workflow execution ID
    /// </summary>
    public required string ExecutionId { get; set; }

    /// <summary>
    /// Current status of the workflow
    /// </summary>
    public required string Status { get; set; }

    /// <summary>
    /// Output from the workflow (if completed)
    /// </summary>
    public Dictionary<string, object>? Output { get; set; }

    /// <summary>
    /// Error message if workflow failed
    /// </summary>
    public string? ErrorMessage { get; set; }
}
