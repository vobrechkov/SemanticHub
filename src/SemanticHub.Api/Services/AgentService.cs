using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using SemanticHub.Api.Configuration;

namespace SemanticHub.Api.Services;

/// <summary>
/// Service for managing AI agents using the Microsoft Agent Framework
/// </summary>
public class AgentService(
    ILogger<AgentService> logger,
    AgentFrameworkOptions options,
    IChatClient chatClient,
    IEnumerable<AIContextProvider> contextProviders)
{
    private readonly IReadOnlyList<AIContextProvider> _contextProviders = contextProviders.ToList();

    /// <summary>
    /// Creates a new AI agent with the specified instructions and tools
    /// </summary>
    public AIAgent CreateAgent(
        string? instructions = null,
        string? name = null,
        IEnumerable<AITool>? tools = null)
    {
        var agentInstructions = instructions ?? options.DefaultAgent.Instructions;
        var agentName = name ?? options.DefaultAgent.Name;

        logger.LogInformation("Creating agent '{Name}' with instructions: {Instructions}", agentName, agentInstructions);

        return chatClient.CreateAIAgent(
            instructions: agentInstructions,
            name: agentName,
            tools: tools?.ToArray()
        );
    }

    /// <summary>
    /// Creates a default agent for the SemanticHub application
    /// </summary>
    public AIAgent CreateDefaultAgent(IEnumerable<AITool>? tools = null)
    {
        return CreateAgent(
            instructions: options.DefaultAgent.Instructions,
            name: options.DefaultAgent.Name,
            tools: tools
        );
    }
}

/// <summary>
/// Factory for creating chat clients
/// </summary>
public class ChatClientFactory(
    ILogger<ChatClientFactory> logger,
    AgentFrameworkOptions options,
    AzureOpenAIClient azureOpenAIClient)
{
    /// <summary>
    /// Creates a chat client using Azure OpenAI
    /// </summary>
    public IChatClient CreateChatClient()
    {
        logger.LogInformation(
            "Creating Azure OpenAI chat client for endpoint: {Endpoint}, deployment: {Deployment}",
            options.AzureOpenAI.Endpoint,
            options.AzureOpenAI.ChatDeployment);

        var chatClient = azureOpenAIClient.GetChatClient(options.AzureOpenAI.ChatDeployment);

        return chatClient.AsIChatClient();
    }
}
