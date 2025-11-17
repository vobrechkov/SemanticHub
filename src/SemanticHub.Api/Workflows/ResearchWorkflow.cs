using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using SemanticHub.Api.Tools;

namespace SemanticHub.Api.Workflows;

/// <summary>
/// Multi-agent workflow for conducting research using the knowledge base
/// Demonstrates sequential workflow: Search → Analyze → Synthesize
/// </summary>
public class ResearchWorkflow(
    ILogger<ResearchWorkflow> logger,
    IChatClient chatClient,
    KnowledgeBaseTools knowledgeBaseTools)
{

    /// <summary>
    /// Creates a multi-agent research workflow
    /// Workflow steps: Search → Analysis → Synthesis
    /// </summary>
    public async Task<AIAgent> CreateWorkflowAsync()
    {
        logger.LogInformation("Creating research workflow");

        // Step 1: Search Agent - Finds relevant information
        var searchAgent = chatClient.CreateAIAgent(
            instructions: @"You are a search specialist. Your job is to:
1. Understand the research question or topic
2. Break it down into key concepts and search terms
3. Search the knowledge base for relevant information
4. Gather multiple sources and documents related to the topic
5. Organize search results by relevance and topic
6. Output: A structured collection of search results with sources",
            name: "SearchAgent",
            tools:
            [
                AIFunctionFactory.Create(knowledgeBaseTools.SearchKnowledgeBase),
                AIFunctionFactory.Create(knowledgeBaseTools.ListDocuments)
            ]
        );

        // Step 2: Analysis Agent - Analyzes and evaluates information
        var analysisAgent = chatClient.CreateAIAgent(
            instructions: @"You are an analysis specialist. Your job is to:
1. Review the search results provided by the search agent
2. Identify key themes, patterns, and insights
3. Evaluate the quality and relevance of information
4. Compare and contrast different sources
5. Identify any gaps or contradictions in the information
6. Extract the most important facts and findings
7. Output: Structured analysis with key findings, themes, and insights",
            name: "AnalysisAgent"
        );

        // Step 3: Synthesis Agent - Creates comprehensive response
        var synthesisAgent = chatClient.CreateAIAgent(
            instructions: @"You are a synthesis specialist. Your job is to:
1. Take the analyzed findings from the analysis agent
2. Create a coherent, comprehensive response
3. Structure the information logically
4. Provide clear explanations and context
5. Include citations and references to sources
6. Ensure the response directly addresses the original question
7. Output: A well-structured, comprehensive answer with citations",
            name: "SynthesisAgent"
        );

        logger.LogInformation("Creating sequential workflow with 3 agents: Search → Analysis → Synthesis");

        // Build sequential workflow
        Workflow workflow = AgentWorkflowBuilder
            .BuildSequential(searchAgent, analysisAgent, synthesisAgent);

        logger.LogInformation("Research workflow created successfully");

        // Convert workflow to agent
        return workflow.AsAgent();
    }

    /// <summary>
    /// Creates a fast research workflow with just search and synthesis (2-step)
    /// </summary>
    public async Task<AIAgent> CreateFastResearchWorkflowAsync()
    {
        logger.LogInformation("Creating fast research workflow (2-step)");

        // Step 1: Enhanced Search Agent (combines search and basic analysis)
        var searchAgent = chatClient.CreateAIAgent(
            instructions: @"You are a research assistant. Your job is to:
1. Search the knowledge base for relevant information
2. Gather and organize the most relevant results
3. Output: Organized search results",
            name: "FastSearchAgent",
            tools:
            [
                AIFunctionFactory.Create(knowledgeBaseTools.SearchKnowledgeBase)
            ]
        );

        // Step 2: Quick Synthesis Agent
        var synthesisAgent = chatClient.CreateAIAgent(
            instructions: @"You are a quick synthesis specialist. Your job is to:
1. Take the search results and create a clear, concise answer
2. Focus on directly answering the question
3. Include key facts and insights
4. Keep the response focused and relevant
5. Output: A concise answer with key information",
            name: "QuickSynthesisAgent"
        );

        logger.LogInformation("Creating fast sequential workflow: Search → Synthesis");

        // Build sequential workflow
        Workflow workflow = AgentWorkflowBuilder
            .BuildSequential(searchAgent, synthesisAgent);

        logger.LogInformation("Fast research workflow created successfully");

        // Convert workflow to agent
        return workflow.AsAgent();
    }
}
