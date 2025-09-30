using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var openai = builder.AddAzureOpenAI("openai");
var storage = builder.AddAzureStorage("storage").RunAsEmulator();
var blobs = storage.AddBlobs("blobs");
var queues = storage.AddQueues("queues");
var chatDeployment = openai.AddDeployment("chat", "gpt-4o-mini", "2024-07-18");
var embeddingDeployment = openai.AddDeployment("embedding", "text-embedding-3-small", "1");
var cache = builder.AddRedis("cache", 16379);
var postgres = builder.AddPostgres("postgres")
    .WithBindMount(
        // Enables `vector` extension
        source: Path.Combine("Infrastructure", "postgres", "init.sql"),
        target: "/docker-entrypoint-initdb.d/init.sql", 
        isReadOnly: true)
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent)
    .WithImage("ankane/pgvector") // Image with `pgvector` support
    .WithImageTag("latest")
    .WithHostPort(15432);

var kernelMemory = builder.AddProject<Projects.SemanticHub_KernelMemoryService>("kernelmemory")
    .WithReference(blobs).WaitFor(blobs)
    .WithReference(queues).WaitFor(queues)
    .WithReference(openai).WaitFor(openai)
    .WithReference(postgres).WaitFor(postgres)
    .WithEnvironment("KernelMemory__Services__AzureBlobs__Container", blobs.Resource.Name)
    .WithEnvironment("KernelMemory__Services__AzureOpenAIText_Deployment", chatDeployment.Resource.Name)
    .WithEnvironment("KernelMemory__Services__AzureOpenAIEmbedding_Deployment", embeddingDeployment.Resource.Name)
    .WithEnvironment("KernelMemory__Services__Postgres__ConnectionString", postgres.Resource.ConnectionStringExpression)
    .WithExternalHttpEndpoints();

if (builder.Environment.IsDevelopment())
{
    // Use connection strings for local development with emulators since Azure Identity is not supported
    kernelMemory = kernelMemory
        .WithEnvironment("KernelMemory__Services__AzureBlobs__ConnectionString", blobs.Resource.ConnectionStringExpression)
        .WithEnvironment("KernelMemory__Services__AzureQueues__ConnectionString", queues.Resource.ConnectionStringExpression);
}

var api = builder.AddProject<Projects.SemanticHub_KnowledgeApi>("knowledge-api")
    .WithReference(kernelMemory).WaitFor(kernelMemory);

//builder.AddProject<Projects.SemanticHub_Web>("web-ui")
//    .WithExternalHttpEndpoints()
//    .WithHttpHealthCheck("/health")
//    .WithReference(api).WaitFor(api)
//    .WithReference(cache).WaitFor(cache);

// Add Next.js React web app
var webApp = builder.AddNpmApp("webapp", "../SemanticHub.WebApp", "dev")
    .WithHttpEndpoint(port: 3000, env: "PORT")
    .WithExternalHttpEndpoints()
    .WithEnvironment("KNOWLEDGE_API_URL", api.GetEndpoint("http"))
    .WithReference(api).WaitFor(api);

builder.Build().Run();
