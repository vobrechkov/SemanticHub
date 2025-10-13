using Azure.Core;
using Azure.Provisioning.CognitiveServices;
using Azure.Provisioning.Search;

const string KnowledgeIndexName = "knowledge-index";
const string ContentFieldName = "content";
const string TitleFieldName = "title";
const string SummaryFieldName = "summary";
const string VectorFieldName = "contentVector";

var builder = DistributedApplication.CreateBuilder(args);

var search = builder.AddAzureSearch("search")
    .ConfigureInfrastructure(infra =>
    {
        var searchService = infra.GetProvisionableResources().OfType<SearchService>().Single();
        searchService.Name = "semhub-eus-dev-search";
        searchService.Location = AzureLocation.EastUS;
        searchService.SearchSkuName = SearchServiceSkuName.Free;
    });

var openai = builder.AddAzureOpenAI("openai")
    .ConfigureInfrastructure(infra =>
    {
        var cogAccount = infra.GetProvisionableResources().OfType<CognitiveServicesAccount>().Single();
        cogAccount.Name = "semhub-eus-dev-openai";
        cogAccount.Location = AzureLocation.EastUS;
    });

var chatDeployment = openai.AddDeployment("chat", "gpt-4o-mini", "2024-07-18");
var embeddingDeployment = openai.AddDeployment("embedding", "text-embedding-3-small", "1");

var ingestion = builder.AddProject<Projects.SemanticHub_IngestionService>("ingestion")
    .WithReference(openai).WaitFor(openai)
    .WithReference(search).WaitFor(search)
    .WithEnvironment("Ingestion__AzureOpenAI__EmbeddingDeployment", embeddingDeployment.Resource.Name)
    .WithEnvironment("Ingestion__AzureSearch__IndexName", KnowledgeIndexName)
    .WithEnvironment("Ingestion__AzureSearch__ContentField", ContentFieldName)
    .WithEnvironment("Ingestion__AzureSearch__TitleField", TitleFieldName)
    .WithEnvironment("Ingestion__AzureSearch__SummaryField", SummaryFieldName)
    .WithEnvironment("Ingestion__AzureSearch__VectorField", VectorFieldName)
    .WithEnvironment("Ingestion__AzureSearch__ParentDocumentField", "parentDocumentId")
    .WithEnvironment("Ingestion__AzureSearch__ChunkTitleField", "chunkTitle")
    .WithEnvironment("Ingestion__AzureSearch__ChunkIndexField", "chunkIndex")
    .WithEnvironment("Ingestion__AzureSearch__MetadataField", "metadataJson");

var agentApi = builder.AddProject<Projects.SemanticHub_Api>("agent-api")
    .WithReference(openai).WaitFor(openai)
    .WithReference(search).WaitFor(search)
    .WithReference(ingestion).WaitFor(ingestion)
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithEnvironment("AgentFramework__AzureOpenAI__ChatDeployment", chatDeployment.Resource.Name)
    .WithEnvironment("AgentFramework__AzureOpenAI__EmbeddingDeployment", embeddingDeployment.Resource.Name)
    .WithEnvironment("AgentFramework__Memory__AzureSearch__IndexName", KnowledgeIndexName)
    .WithEnvironment("AgentFramework__Memory__AzureSearch__ContentField", ContentFieldName)
    .WithEnvironment("AgentFramework__Memory__AzureSearch__TitleField", TitleFieldName)
    .WithEnvironment("AgentFramework__Memory__AzureSearch__SummaryField", SummaryFieldName)
    .WithEnvironment("AgentFramework__Memory__AzureSearch__VectorField", VectorFieldName)
    .WithEnvironment("AgentFramework__Memory__AzureSearch__ParentDocumentField", "parentDocumentId")
    .WithEnvironment("AgentFramework__Memory__AzureSearch__ChunkTitleField", "chunkTitle")
    .WithEnvironment("AgentFramework__Memory__AzureSearch__ChunkIndexField", "chunkIndex")
    .WithEnvironment("AgentFramework__Memory__AzureSearch__MetadataField", "metadataJson");

var webApp = builder.AddNpmApp("webapp", "../SemanticHub.WebApp", "dev")
    .WithHttpEndpoint(port: 3000, env: "PORT")
    .WithExternalHttpEndpoints()
    .WithEnvironment("AGENT_API_URL", agentApi.GetEndpoint("http"))
    .WithReference(agentApi).WaitFor(agentApi);

builder.Build().Run();
