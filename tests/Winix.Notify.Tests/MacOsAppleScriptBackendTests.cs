#nullable enable
using Xunit;
using Winix.Notify;

namespace Winix.Notify.Tests;

public class MacOsAppleScriptBackendTests
{
    [Fact]
    public void Backend_Name_IsMacosOsascript()
    {
        var b = new MacOsAppleScriptBackend();
        Assert.Equal("macos-osascript", b.Name);
    }

    [Theory]
    [InlineData("hello", "hello")]
    [InlineData("with \"quotes\"", "with \\\"quotes\\\"")]
    [InlineData("with \\backslash", "with \\\\backslash")]
    public void EscapeForApplescript_ProducesCorrectQuoting(string input, string expected)
    {
        // Backslash MUST be doubled before the quote-escape, otherwise " -> \" introduces
        // an unescaped backslash that AppleScript would interpret as a string-escape escape.
        Assert.Equal(expected, MacOsAppleScriptBackend.EscapeForApplescript(input));
    }
}
