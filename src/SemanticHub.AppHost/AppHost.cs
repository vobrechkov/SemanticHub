using Azure.Core;
using Azure.Provisioning.CognitiveServices;
using Azure.Provisioning.Search;

var builder = DistributedApplication.CreateBuilder(args);

var search = builder.AddAzureSearch("search")
    .ConfigureInfrastructure(infra =>
    {
        var searchService = infra.GetProvisionableResources().OfType<SearchService>().Single();
        searchService.Name = "semhub-eus-dev-search";
        searchService.Location = AzureLocation.EastUS;
        searchService.SearchSkuName = SearchServiceSkuName.Free;
        searchService.IsLocalAuthDisabled = true;
    });

var openai = builder.AddAzureOpenAI("openai")
    .ConfigureInfrastructure(infra =>
    {
        var cogAccount = infra.GetProvisionableResources().OfType<CognitiveServicesAccount>().Single();
        cogAccount.Name = "semhub-eus-dev-openai";
        cogAccount.Location = AzureLocation.EastUS;
        cogAccount.Properties.DisableLocalAuth = true;

    });

var chatDeployment = openai.AddDeployment("chat", "gpt-4o-mini", "2024-07-18");
var embeddingDeployment = openai.AddDeployment("embedding", "text-embedding-3-small", "1");

var ingestion = builder.AddProject<Projects.SemanticHub_IngestionService>("ingestion")
    .WithReference(openai).WaitFor(openai)
    .WithReference(search).WaitFor(search)
    .WithEnvironment("Ingestion__AzureOpenAI__EmbeddingDeployment", embeddingDeployment.Resource.Name);

var agentApi = builder.AddProject<Projects.SemanticHub_Api>("agent-api")
    .WithReference(openai).WaitFor(openai)
    .WithReference(search).WaitFor(search)
    .WithReference(ingestion).WaitFor(ingestion)
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithEnvironment("AgentFramework__AzureOpenAI__ChatDeployment", chatDeployment.Resource.Name)
    .WithEnvironment("AgentFramework__AzureOpenAI__EmbeddingDeployment", embeddingDeployment.Resource.Name);

var webApp = builder.AddNpmApp("webapp", "../SemanticHub.WebApp", "dev")
    .WithHttpEndpoint(port: 3000, env: "PORT")
    .WithExternalHttpEndpoints()
    .WithEnvironment("AGENT_API_URL", agentApi.GetEndpoint("http"))
    .WithReference(agentApi).WaitFor(agentApi);

builder.Build().Run();
