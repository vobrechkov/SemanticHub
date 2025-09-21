var builder = DistributedApplication.CreateBuilder(args);

var cache = builder.AddRedis("cache");

var kernelMemory = builder.AddProject<Projects.SemanticHub_KernelMemoryService>("memory")
    .WithReference(cache).WaitFor(cache);

var knowledgeApi = builder.AddProject<Projects.SemanticHub_KnowledgeApi>("knowledge-api")
    .WithReference(kernelMemory).WaitFor(kernelMemory);

builder.AddProject<Projects.SemanticHub_Web>("web-ui")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(cache).WaitFor(cache)
    .WithReference(knowledgeApi).WaitFor(knowledgeApi);

builder.Build().Run();
