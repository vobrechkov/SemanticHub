using HtmlAgilityPack;
using SemanticHub.IngestionService.Services.Processors;
using Xunit;

namespace SemanticHub.Tests.Services;

/// <summary>
/// Tests for the ContentScorer class that implements Mozilla Readability-style scoring.
/// </summary>
public class ContentScorerTests
{
    private readonly ContentScorer _scorer;

    public ContentScorerTests()
    {
        _scorer = new ContentScorer();
    }

    [Fact]
    public void CalculateScore_ArticleElement_ReturnsHighScore()
    {
        // Arrange
        var html = @"
            <article class='content'>
                <h1>Title</h1>
                <p>This is a paragraph with some content, commas, and more text.</p>
                <p>Another paragraph with even more content to increase the score.</p>
            </article>";
        var doc = CreateDocument(html);
        var article = doc.DocumentNode.SelectSingleNode("//article");

        // Act
        var score = _scorer.CalculateScore(article!);

        // Assert
        Assert.True(score > 50, $"Article should have high score, got {score}");
    }

    [Fact]
    public void CalculateScore_NavigationElement_ReturnsLowScore()
    {
        // Arrange
        var html = @"
            <div class='navigation'>
                <a href='/home'>Home</a>
                <a href='/about'>About</a>
                <a href='/contact'>Contact</a>
            </div>";
        var doc = CreateDocument(html);
        var nav = doc.DocumentNode.SelectSingleNode("//div");

        // Act
        var score = _scorer.CalculateScore(nav!);

        // Assert
        Assert.True(score < 0, $"Navigation should have negative score, got {score}");
    }

    [Fact]
    public void CalculateScore_SidebarElement_ReturnsNegativeScore()
    {
        // Arrange
        var html = @"
            <aside class='sidebar'>
                <div class='widget'>Advertisement</div>
                <div class='widget'>Social Links</div>
            </aside>";
        var doc = CreateDocument(html);
        var sidebar = doc.DocumentNode.SelectSingleNode("//aside");

        // Act
        var score = _scorer.CalculateScore(sidebar!);

        // Assert
        Assert.True(score < 0, $"Sidebar should have negative score, got {score}");
    }

    [Fact]
    public void CalculateScore_MainElement_ReturnsHighScore()
    {
        // Arrange
        var html = @"
            <main>
                <h1>Article Title</h1>
                <p>This is the main content area with significant text, multiple commas, and proper prose structure.</p>
                <p>More paragraphs indicate this is likely real content.</p>
                <p>Even more content to ensure high scoring.</p>
            </main>";
        var doc = CreateDocument(html);
        var main = doc.DocumentNode.SelectSingleNode("//main");

        // Act
        var score = _scorer.CalculateScore(main!);

        // Assert
        Assert.True(score > 40, $"Main element should have high score, got {score}");
    }

    [Fact]
    public void CalculateScore_CommentsSection_ReturnsNegativeScore()
    {
        // Arrange
        var html = @"
            <div class='comments'>
                <div class='comment'>Comment 1</div>
                <div class='comment'>Comment 2</div>
            </div>";
        var doc = CreateDocument(html);
        var comments = doc.DocumentNode.SelectSingleNode("//div");

        // Act
        var score = _scorer.CalculateScore(comments!);

        // Assert
        Assert.True(score < 0, $"Comments section should have negative score, got {score}");
    }

    [Fact]
    public void CalculateLinkDensity_HighLinkContent_ReturnsHighDensity()
    {
        // Arrange
        var html = @"
            <div>
                <a href='/link1'>Link 1</a>
                <a href='/link2'>Link 2</a>
                <a href='/link3'>Link 3</a>
                Short text.
            </div>";
        var doc = CreateDocument(html);
        var div = doc.DocumentNode.SelectSingleNode("//div");

        // Act
        var linkDensity = _scorer.CalculateLinkDensity(div!);

        // Assert
        Assert.True(linkDensity > 0.5, $"Link density should be high, got {linkDensity:F2}");
    }

    [Fact]
    public void CalculateLinkDensity_LowLinkContent_ReturnsLowDensity()
    {
        // Arrange
        var html = @"
            <div>
                <p>This is a long paragraph with lots of text content and only one link at the end.</p>
                <p>More text content without any links at all in this paragraph.</p>
                <p>And even more text to ensure link density is low.</p>
                <a href='/single'>One link</a>
            </div>";
        var doc = CreateDocument(html);
        var div = doc.DocumentNode.SelectSingleNode("//div");

        // Act
        var linkDensity = _scorer.CalculateLinkDensity(div!);

        // Assert
        Assert.True(linkDensity < 0.2, $"Link density should be low, got {linkDensity:F2}");
    }

    [Fact]
    public void CalculateLinkDensity_NoLinks_ReturnsZero()
    {
        // Arrange
        var html = @"
            <div>
                <p>Text without any links at all.</p>
            </div>";
        var doc = CreateDocument(html);
        var div = doc.DocumentNode.SelectSingleNode("//div");

        // Act
        var linkDensity = _scorer.CalculateLinkDensity(div!);

        // Assert
        Assert.Equal(0.0, linkDensity);
    }

    [Fact]
    public void CalculateLinkDensity_OnlyLinks_ReturnsOne()
    {
        // Arrange
        var html = @"
            <div>
                <a href='/link1'>Link</a>
                <a href='/link2'>Link</a>
            </div>";
        var doc = CreateDocument(html);
        var div = doc.DocumentNode.SelectSingleNode("//div");

        // Act
        var linkDensity = _scorer.CalculateLinkDensity(div!);

        // Assert
        Assert.True(linkDensity >= 0.9, $"Link density should be near 1.0, got {linkDensity:F2}");
    }

    [Fact]
    public void CalculateTextDensity_RichContent_ReturnsHighDensity()
    {
        // Arrange
        var html = @"
            <article>
                This is text heavy content without much markup.
                Just plain text paragraphs.
            </article>";
        var doc = CreateDocument(html);
        var article = doc.DocumentNode.SelectSingleNode("//article");

        // Act
        var textDensity = _scorer.CalculateTextDensity(article!);

        // Assert
        Assert.True(textDensity > 0.5, $"Text density should be high for plain content, got {textDensity:F2}");
    }

    [Fact]
    public void CalculateTextDensity_HeavyMarkup_ReturnsLowDensity()
    {
        // Arrange
        var html = @"
            <div>
                <div><span><strong><em><a href='#'>Link</a></em></strong></span></div>
            </div>";
        var doc = CreateDocument(html);
        var div = doc.DocumentNode.SelectSingleNode("//div");

        // Act
        var textDensity = _scorer.CalculateTextDensity(div!);

        // Assert
        Assert.True(textDensity < 0.3, $"Text density should be low for heavy markup, got {textDensity:F2}");
    }

    [Fact]
    public void GetConfidenceScore_HighScore_ReturnsHighConfidence()
    {
        // Arrange
        var html = @"
            <article class='content main'>
                <h1>Title</h1>
                <p>Paragraph one with commas, proper prose, and good length to score well.</p>
                <p>Paragraph two continues the pattern.</p>
                <p>Paragraph three adds even more content.</p>
            </article>";
        var doc = CreateDocument(html);
        var article = doc.DocumentNode.SelectSingleNode("//article");

        // Act
        var confidence = _scorer.GetConfidenceScore(article!);

        // Assert
        Assert.True(confidence > 0.7, $"Confidence should be high for good content, got {confidence:F2}");
    }

    [Fact]
    public void GetConfidenceScore_NegativeScore_ReturnsLowConfidence()
    {
        // Arrange
        var html = @"
            <div class='advertisement banner sponsored'>
                <a href='/ad'>Click here!</a>
            </div>";
        var doc = CreateDocument(html);
        var div = doc.DocumentNode.SelectSingleNode("//div");

        // Act
        var confidence = _scorer.GetConfidenceScore(div!);

        // Assert
        Assert.True(confidence < 0.3, $"Confidence should be low for ads, got {confidence:F2}");
    }

    [Fact]
    public void CalculateScore_PositiveClassPatterns_IncreasesScore()
    {
        // Arrange
        var html1 = "<div><p>Some text</p></div>";
        var html2 = "<div class='content'><p>Some text</p></div>";
        var doc1 = CreateDocument(html1);
        var doc2 = CreateDocument(html2);
        var div1 = doc1.DocumentNode.SelectSingleNode("//div");
        var div2 = doc2.DocumentNode.SelectSingleNode("//div");

        // Act
        var score1 = _scorer.CalculateScore(div1!);
        var score2 = _scorer.CalculateScore(div2!);

        // Assert
        Assert.True(score2 > score1, $"Content class should increase score: {score1} -> {score2}");
    }

    [Fact]
    public void CalculateScore_NegativeClassPatterns_DecreasesScore()
    {
        // Arrange
        var html1 = "<div><p>Some text</p></div>";
        var html2 = "<div class='advertisement'><p>Some text</p></div>";
        var doc1 = CreateDocument(html1);
        var doc2 = CreateDocument(html2);
        var div1 = doc1.DocumentNode.SelectSingleNode("//div");
        var div2 = doc2.DocumentNode.SelectSingleNode("//div");

        // Act
        var score1 = _scorer.CalculateScore(div1!);
        var score2 = _scorer.CalculateScore(div2!);

        // Assert
        Assert.True(score2 < score1, $"Advertisement class should decrease score: {score1} -> {score2}");
    }

    [Fact]
    public void CalculateScore_RoleMain_IncreasesScore()
    {
        // Arrange
        var html1 = "<div><p>Some text</p></div>";
        var html2 = "<div role='main'><p>Some text</p></div>";
        var doc1 = CreateDocument(html1);
        var doc2 = CreateDocument(html2);
        var div1 = doc1.DocumentNode.SelectSingleNode("//div");
        var div2 = doc2.DocumentNode.SelectSingleNode("//div");

        // Act
        var score1 = _scorer.CalculateScore(div1!);
        var score2 = _scorer.CalculateScore(div2!);

        // Assert
        Assert.True(score2 > score1, $"role=main should increase score: {score1} -> {score2}");
    }

    [Fact]
    public void CalculateScore_MoreParagraphs_IncreasesScore()
    {
        // Arrange
        var html1 = "<div><p>Paragraph 1</p></div>";
        var html2 = "<div><p>Paragraph 1</p><p>Paragraph 2</p><p>Paragraph 3</p></div>";
        var doc1 = CreateDocument(html1);
        var doc2 = CreateDocument(html2);
        var div1 = doc1.DocumentNode.SelectSingleNode("//div");
        var div2 = doc2.DocumentNode.SelectSingleNode("//div");

        // Act
        var score1 = _scorer.CalculateScore(div1!);
        var score2 = _scorer.CalculateScore(div2!);

        // Assert
        Assert.True(score2 > score1, $"More paragraphs should increase score: {score1} -> {score2}");
    }

    [Fact]
    public void CalculateScore_MoreImagesThanParagraphs_DecreasesScore()
    {
        // Arrange
        var html = @"
            <div>
                <img src='1.jpg' alt='Image 1'/>
                <img src='2.jpg' alt='Image 2'/>
                <img src='3.jpg' alt='Image 3'/>
                <p>One paragraph</p>
            </div>";
        var doc = CreateDocument(html);
        var div = doc.DocumentNode.SelectSingleNode("//div");

        // Act
        var score = _scorer.CalculateScore(div!);

        // Assert - Image galleries should be penalized
        Assert.True(score < 20, $"Image-heavy content should have lower score, got {score}");
    }

    private static HtmlDocument CreateDocument(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        return doc;
    }
}
