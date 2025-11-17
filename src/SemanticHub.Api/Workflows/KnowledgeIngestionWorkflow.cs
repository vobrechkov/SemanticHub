using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using SemanticHub.Api.Tools;

namespace SemanticHub.Api.Workflows;

/// <summary>
/// Multi-step workflow for ingesting and processing documents into the knowledge base
/// This demonstrates the Agent Framework's workflow orchestration capabilities
/// </summary>
public class KnowledgeIngestionWorkflow(
    ILogger<KnowledgeIngestionWorkflow> logger,
    IChatClient chatClient,
    KnowledgeBaseTools knowledgeBaseTools,
    IngestionTools ingestionTools)
{
    /// <summary>
    /// Creates a multi-agent workflow for document ingestion
    /// Workflow steps: Validation → Extraction → Indexing → Verification
    /// </summary>
    public AIAgent CreateWorkflow()
    {
        logger.LogInformation("Creating knowledge ingestion workflow");

        // Step 1: Document Validator Agent
        var validatorAgent = chatClient.CreateAIAgent(
            instructions: """
                          You are a document validator. Your job is to:
                          1. Check if the input is valid document content
                          2. Verify the document format and structure
                          3. Ensure the document is not empty or corrupted
                          4. Extract basic metadata (title, type, size estimate)
                          5. Output: 'VALID: <metadata>' or 'INVALID: <reason>'
                          """,
            name: "DocumentValidator"
        );

        // Step 2: Content Extractor Agent
        var extractorAgent = chatClient.CreateAIAgent(
            instructions: """
                          You are a content extractor. Your job is to:
                          1. Extract the main text content from the validated document
                          2. Identify key sections, headings, and structure
                          3. Extract any metadata (author, date, keywords)
                          4. Clean and normalize the text
                          5. Prepare content for chunking and indexing
                          6. Output: Extracted and structured content with metadata
                          """,
            name: "ContentExtractor"
        );

        // Step 3: Indexer Agent (with tools for Azure AI Search ingestion)
        var indexerAgent = chatClient.CreateAIAgent(
            instructions: """
                          You are a document indexer. Your job is to:
                          1. Take extracted content and break it into appropriate chunks
                          2. Generate meaningful summaries for each chunk
                          3. Call the appropriate ingestion tool based on content type:
                             - For OpenAPI specifications (YAML/JSON): use IngestOpenApiSpecAsync
                             - For Markdown content: use IngestMarkdownDocumentAsync
                             - For web pages: use IngestWebPageAsync
                          4. Track the document ID and storage status
                          5. Output: 'INDEXED: <document-id>' with confirmation
                          """,
            name: "DocumentIndexer",
            tools:
            [
                AIFunctionFactory.Create(ingestionTools.IngestMarkdownDocumentAsync),
                AIFunctionFactory.Create(ingestionTools.IngestWebPageAsync),
                AIFunctionFactory.Create(ingestionTools.IngestOpenApiSpecAsync),
                AIFunctionFactory.Create(knowledgeBaseTools.GetDocumentStatus),
                AIFunctionFactory.Create(knowledgeBaseTools.ListDocuments)
            ]
        );

        // Step 4: Verification Agent
        var verifierAgent = chatClient.CreateAIAgent(
            instructions: """
                          You are a document verification agent. Your job is to:
                          1. Verify that the document was successfully indexed
                          2. Check that the content is searchable
                          3. Test search with key terms from the document
                          4. Provide a verification report
                          5. Output: 'VERIFIED: <document-id>' or 'FAILED: <reason>'
                          """,
            name: "DocumentVerifier",
            tools:
            [
                AIFunctionFactory.Create(knowledgeBaseTools.SearchKnowledgeBase),
                AIFunctionFactory.Create(knowledgeBaseTools.GetDocumentStatus)
            ]
        );

        logger.LogInformation("Creating sequential workflow with 4 agents: Validator → Extractor → Indexer → Verifier");

        // Build sequential workflow
        var workflow = AgentWorkflowBuilder
            .BuildSequential(validatorAgent, extractorAgent, indexerAgent, verifierAgent);

        logger.LogInformation("Workflow created successfully");

        // Convert workflow to agent
        return workflow.AsAgent();
    }
}

/// <summary>
/// Result model for workflow execution
/// </summary>
public class WorkflowStepResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public Dictionary<string, object>? Data { get; set; }
}
