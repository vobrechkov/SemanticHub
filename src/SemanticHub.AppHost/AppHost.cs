using Aspire.Hosting.Azure;
using Azure.Core;
using Azure.Provisioning.CognitiveServices;
using Azure.Provisioning.Search;
using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

var enableAzureSearch = builder.Configuration.GetValue("Features:AzureSearch:Enabled", true);
var enableOpenSearch = builder.Configuration.GetValue("Features:OpenSearch:Enabled", false);

IResourceBuilder<AzureSearchResource>? search = null;
if (enableAzureSearch)
{
    search = builder.AddAzureSearch("search")
        .ConfigureInfrastructure(infra =>
        {
            var searchService = infra.GetProvisionableResources().OfType<SearchService>().Single();
            searchService.Name = "semhub-eus-dev-search";
            searchService.Location = AzureLocation.EastUS;
            searchService.SearchSkuName = SearchServiceSkuName.Free;
            searchService.IsLocalAuthDisabled = true;
        });
}

IResourceBuilder<ContainerResource>? openSearch = null;
if (enableOpenSearch)
{
    openSearch = builder.AddContainer("opensearch", "opensearchproject/opensearch", "2.12.0")
        .WithHttpEndpoint(name: "http", port: 9200, targetPort: 9200)
        .WithEnvironment("discovery.type", "single-node")
        .WithEnvironment("plugins.security.disabled", "true")
        .WithEnvironment("plugins.security.allow_default_init_securityindex", "true")
        .WithEnvironment("OPENSEARCH_JAVA_OPTS", "-Xms512m -Xmx512m")
        .WithContainerRuntimeArgs("--health-cmd", "curl --silent --fail localhost:9200/_cluster/health || exit 1", "--health-interval", "10s", "--health-timeout", "10s", "--health-retries", "10");
}

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

// Add Azure Blob Storage with Azurite emulator for local development
var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator(container =>
    {
        // Configure Azurite to use default ports
        container.WithLifetime(ContainerLifetime.Persistent);
    })
    .ConfigureInfrastructure(infra =>
    {
        var storageAccount = infra.GetProvisionableResources()
            .OfType<Azure.Provisioning.Storage.StorageAccount>()
            .Single();
        storageAccount.Name = "semhubeusdevstorage";
        storageAccount.Location = AzureLocation.EastUS;
    });

var blobs = storage.AddBlobs("blobs");

IResourceBuilder<ProjectResource>? ingestion = null;
if (enableAzureSearch && search is not null)
{
    ingestion = builder.AddProject<Projects.SemanticHub_IngestionService>("ingestion")
        .WithReference(openai).WaitFor(openai)
        .WithReference(search).WaitFor(search)
        .WithReference(blobs).WaitFor(storage)
        .WithEnvironment("Ingestion__AzureOpenAI__EmbeddingDeployment", embeddingDeployment.Resource.Name);
}

var agentApi = builder.AddProject<Projects.SemanticHub_Api>("agent-api")
    .WithReference(openai).WaitFor(openai)
    .WithReference(blobs).WaitFor(storage)
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithEnvironment("AgentFramework__AzureOpenAI__ChatDeployment", chatDeployment.Resource.Name)
    .WithEnvironment("AgentFramework__AzureOpenAI__EmbeddingDeployment", embeddingDeployment.Resource.Name);

if (search is not null)
{
    agentApi.WithReference(search).WaitFor(search);
}

if (ingestion is not null)
{
    agentApi.WithReference(ingestion).WaitFor(ingestion);
}

if (openSearch is not null)
{
    agentApi.WaitFor(openSearch)
        .WithEnvironment("AgentFramework__Memory__Provider", "OpenSearch")
        .WithEnvironment("AgentFramework__Memory__OpenSearch__Endpoint", openSearch.GetEndpoint("http"));
}

if (!enableOpenSearch)
{
    agentApi.WithEnvironment("AgentFramework__Memory__Provider", "AzureSearch");
}

var webApp = builder.AddNpmApp("webapp", "../SemanticHub.WebApp", "dev")
    .WithHttpEndpoint(port: 3000, env: "PORT")
    .WithExternalHttpEndpoints()
    .WithEnvironment("AGENT_API_URL", agentApi.GetEndpoint("http"))
    .WithReference(agentApi).WaitFor(agentApi);

builder.Build().Run();
