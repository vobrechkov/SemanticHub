namespace SemanticHub.Api.Configuration;

/// <summary>
/// Configuration options for the Microsoft Agent Framework integration
/// </summary>
public class AgentFrameworkOptions
{
    public const string SectionName = "AgentFramework";

    public AzureOpenAIOptions AzureOpenAI { get; set; } = new();
    public DefaultAgentOptions DefaultAgent { get; set; } = new();
    public MemoryOptions Memory { get; set; } = new();
}

public class AzureOpenAIOptions
{
    public string Endpoint { get; set; } = string.Empty;
    public string ChatDeployment { get; set; } = string.Empty;
    public string EmbeddingDeployment { get; set; } = string.Empty;
    public string? ApiKey { get; set; }
}

public class DefaultAgentOptions
{
    public string Name { get; set; } = "SemanticHub Assistant";
    public string Instructions { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4o-mini";
}

public class MemoryOptions
{
    public bool EnableMem0 { get; set; } = false;
    public bool EnableWhiteboard { get; set; } = true;
    public AzureSearchMemoryOptions AzureSearch { get; set; } = new();
}

public class AzureSearchMemoryOptions
{
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string IndexName { get; set; } = string.Empty;
    public string KeyField { get; set; } = "id";
    public string ContentField { get; set; } = "content";
    public string? TitleField { get; set; } = "title";
    public string? SummaryField { get; set; }
    public string? SemanticConfiguration { get; set; }
    public string? VectorField { get; set; }
    public int VectorKNearestNeighbors { get; set; } = 8;
    public int MaxResults { get; set; } = 5;
    public double MinRelevance { get; set; } = 0.6;
    public string ParentDocumentField { get; set; } = "parentDocumentId";
    public string? ChunkTitleField { get; set; } = "chunkTitle";
    public string? ChunkIndexField { get; set; } = "chunkIndex";
    public string? MetadataField { get; set; } = "metadataJson";
}
