using Winix.HCat.Handlers;
using Xunit;

namespace Winix.HCat.Tests;

public class AcceptsHtmlTests
{
    [Theory]
    [InlineData("text/html")]
    [InlineData("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8")]  // typical browser nav
    [InlineData("application/xml, text/html")]
    [InlineData("text/html; q=1.0")]
    [InlineData("  text/html  ")]                                                   // whitespace tolerated
    public void True_when_text_html_explicitly_listed(string accept)
    {
        Assert.True(ServeConfig.AcceptsHtml(accept));
    }

    [Theory]
    [InlineData("*/*")]                  // curl/wget default — NOT a navigation
    [InlineData("application/json")]
    [InlineData("image/png")]
    [InlineData("text/*")]               // type wildcard is not an explicit text/html
    [InlineData("text/htmlx")]           // must not substring-match
    [InlineData("")]
    [InlineData(null)]
    public void False_when_html_not_explicitly_listed(string? accept)
    {
        Assert.False(ServeConfig.AcceptsHtml(accept));
    }
}
