using Wantodo.Core;
using Xunit;

namespace Wantodo.Core.Tests;

public sealed class TextLinksTests
{
    [Fact]
    public void Extract_FindsHttpAndHttps()
    {
        var links = TextLinks.Extract("看这个 https://example.com/a 和 http://b.test/x?y=1 都行");

        Assert.Equal(new[] { "https://example.com/a", "http://b.test/x?y=1" }, links);
    }

    [Theory]
    [InlineData("www.example.com", "https://www.example.com")]
    [InlineData("见 www.example.com/path。", "https://www.example.com/path")]
    [InlineData("https://example.com/end).", "https://example.com/end")]
    public void Extract_TrimsPunctuationAndPrefixesWww(string text, string expected)
    {
        Assert.Equal(new[] { expected }, TextLinks.Extract(text));
    }

    [Fact]
    public void Extract_DeduplicatesCaseInsensitively()
    {
        var links = TextLinks.Extract("https://a.test/x HTTPS://A.TEST/x");

        Assert.Single(links);
    }

    [Fact]
    public void Extract_IgnoresSchemeInsideWord()
    {
        Assert.Empty(TextLinks.Extract("abchttp://broken"));
    }

    [Fact]
    public void Extract_ReturnsEmptyForBlankOrPlainText()
    {
        Assert.Empty(TextLinks.Extract(null));
        Assert.Empty(TextLinks.Extract("   "));
        Assert.Empty(TextLinks.Extract("没有链接的一段话，1.2.3 也不是网址"));
    }

    [Fact]
    public void Extract_KeepsMultipleLinksInOrder()
    {
        var links = TextLinks.Extract("https://one.test http://two.test www.three.test");

        Assert.Equal(
            new[] { "https://one.test", "http://two.test", "https://www.three.test" },
            links);
    }
}
