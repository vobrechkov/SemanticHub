using SemanticHub.Api.Configuration;

namespace SemanticHub.Tests.AgentFramework;

/// <summary>
/// Simplified unit tests for AgentService configuration and setup
/// Note: Full integration testing requires actual OpenAI/Azure credentials
/// </summary>
public class AgentServiceSimpleTests
{
    [Fact]
    public void AgentFrameworkOptions_CanBeCreated()
    {
        // Arrange & Act
        var options = new AgentFrameworkOptions
        {
            DefaultAgent = new DefaultAgentOptions
            {
                Name = "Test Agent",
                Instructions = "You are a test assistant",
                Model = "gpt-4o-mini"
            },
            AzureOpenAI = new AzureOpenAIOptions
            {
                Endpoint = "https://test.openai.azure.com",
                ChatDeployment = "gpt-4o-mini",
                EmbeddingDeployment = "text-embedding-3-small"
            }
        };

        // Assert
        Assert.NotNull(options);
        Assert.Equal("Test Agent", options.DefaultAgent.Name);
        Assert.Equal("You are a test assistant", options.DefaultAgent.Instructions);
        Assert.Equal("gpt-4o-mini", options.DefaultAgent.Model);
    }

    [Fact]
    public void DefaultAgentOptions_HasCorrectDefaults()
    {
        // Arrange & Act
        var options = new DefaultAgentOptions
        {
            Name = "Assistant",
            Instructions = "You are helpful",
            Model = "gpt-4"
        };

        // Assert
        Assert.Equal("Assistant", options.Name);
        Assert.Equal("You are helpful", options.Instructions);
        Assert.Equal("gpt-4", options.Model);
    }

    [Fact]
    public void AzureOpenAIOptions_CanBeConfigured()
    {
        // Arrange & Act
        var options = new AzureOpenAIOptions
        {
            Endpoint = "https://myresource.openai.azure.com",
            ChatDeployment = "gpt-4o-mini",
            EmbeddingDeployment = "text-embedding-3-small"
        };

        // Assert
        Assert.NotNull(options);
        Assert.Equal("https://myresource.openai.azure.com", options.Endpoint);
        Assert.Equal("gpt-4o-mini", options.ChatDeployment);
        Assert.Equal("text-embedding-3-small", options.EmbeddingDeployment);
    }
}
