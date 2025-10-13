using Microsoft.Extensions.Logging;
using Moq;
using SemanticHub.Api.Configuration;
using SemanticHub.Api.Memory;
using SemanticHub.Api.Services;
using SemanticHub.Api.Tools;
using SemanticHub.Api.Workflows;

namespace SemanticHub.Tests.Workflows;

/// <summary>
/// Simple smoke tests ensuring workflow constructors remain wired with current dependencies.
/// </summary>
public class WorkflowConfigurationTests
{
    private static KnowledgeBaseTools CreateKnowledgeBaseTools()
    {
        var knowledgeStore = new Mock<IKnowledgeStore>();
        var logger = new Mock<ILogger<KnowledgeBaseTools>>();
        var options = new AgentFrameworkOptions();

        return new KnowledgeBaseTools(logger.Object, knowledgeStore.Object, options);
    }

    private static IngestionTools CreateIngestionTools()
    {
        var handler = new StubHttpHandler();
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/")
        };

        var ingestionClientLogger = new Mock<ILogger<IngestionClient>>();
        var ingestionClient = new IngestionClient(httpClient, ingestionClientLogger.Object);

        var ingestionToolsLogger = new Mock<ILogger<IngestionTools>>();
        return new IngestionTools(ingestionClient, ingestionToolsLogger.Object);
    }

    [Fact]
    public void KnowledgeIngestionWorkflow_CanBeCreated()
    {
        var workflowLogger = new Mock<ILogger<KnowledgeIngestionWorkflow>>();
        var chatClient = Mock.Of<Microsoft.Extensions.AI.IChatClient>();
        var knowledgeTools = CreateKnowledgeBaseTools();
        var ingestionTools = CreateIngestionTools();

        var workflow = new KnowledgeIngestionWorkflow(
            workflowLogger.Object,
            chatClient,
            knowledgeTools,
            ingestionTools);

        Assert.NotNull(workflow);
    }

    [Fact]
    public void ResearchWorkflow_CanBeCreated()
    {
        var workflowLogger = new Mock<ILogger<ResearchWorkflow>>();
        var chatClient = Mock.Of<Microsoft.Extensions.AI.IChatClient>();
        var knowledgeTools = CreateKnowledgeBaseTools();

        var workflow = new ResearchWorkflow(
            workflowLogger.Object,
            chatClient,
            knowledgeTools);

        Assert.NotNull(workflow);
    }
}

internal sealed class StubHttpHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"success":true,"documentId":"test-doc","chunksIndexed":0}""")
        };

        return Task.FromResult(response);
    }
}
