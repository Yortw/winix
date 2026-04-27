using Winix.Schedule;
using Xunit;

namespace Winix.Schedule.Tests;

/// <summary>
/// Pins the Windows CRT command-line escape contract used to build schtasks /TR.
/// The CRT rules (CommandLineToArgvW) are subtle — backslash-quote handling depends on
/// whether the count is even or odd, and trailing backslashes before a closing quote
/// must be doubled. These tests catch regressions on the trailing-backslash injection
/// foot-gun CLAUDE.md warns against.
/// </summary>
public sealed class SchtasksArgEscapeTests
{
    [Fact]
    public void EscapeWindowsArg_NoSpecialChars_ReturnsUnchanged()
    {
        Assert.Equal("dotnet", SchtasksBackend.EscapeWindowsArg("dotnet"));
        Assert.Equal("build", SchtasksBackend.EscapeWindowsArg("build"));
        Assert.Equal("--verbose", SchtasksBackend.EscapeWindowsArg("--verbose"));
    }

    [Fact]
    public void EscapeWindowsArg_EmptyString_ReturnsEmptyQuotes()
    {
        Assert.Equal("\"\"", SchtasksBackend.EscapeWindowsArg(""));
    }

    [Fact]
    public void EscapeWindowsArg_SpaceTriggersQuoting()
    {
        Assert.Equal("\"hello world\"", SchtasksBackend.EscapeWindowsArg("hello world"));
    }

    [Fact]
    public void EscapeWindowsArg_TabTriggersQuoting()
    {
        Assert.Equal("\"a\tb\"", SchtasksBackend.EscapeWindowsArg("a\tb"));
    }

    [Fact]
    public void EscapeWindowsArg_EmbeddedDoubleQuoteEscapesWithBackslash()
    {
        // arg: it's "quoted"   →   "it's \"quoted\""
        Assert.Equal("\"it's \\\"quoted\\\"\"", SchtasksBackend.EscapeWindowsArg("it's \"quoted\""));
    }

    [Fact]
    public void EscapeWindowsArg_TrailingBackslashIsDoubled()
    {
        // The canonical Windows trap. arg ends with \, must be doubled before closing quote.
        // arg: C:\Program Files\   →   "C:\Program Files\\"
        Assert.Equal("\"C:\\Program Files\\\\\"", SchtasksBackend.EscapeWindowsArg(@"C:\Program Files\"));
    }

    [Fact]
    public void EscapeWindowsArg_BackslashBeforeQuoteIsDoubled()
    {
        // arg: a\"b  →  "a\\\"b"  (one literal backslash, then escaped quote)
        Assert.Equal("\"a\\\\\\\"b\"", SchtasksBackend.EscapeWindowsArg("a\\\"b"));
    }

    [Fact]
    public void EscapeWindowsArg_StandaloneBackslashIsLiteral()
    {
        // arg with backslash but no whitespace/quote: no quoting needed at all.
        Assert.Equal(@"C:\foo", SchtasksBackend.EscapeWindowsArg(@"C:\foo"));
    }

    [Fact]
    public void EscapeWindowsArg_BackslashWithSpace_BackslashLiteralButQuoted()
    {
        // Space forces quoting; backslash in middle stays literal (1 backslash, no quote following).
        Assert.Equal(@"""C:\Program Files""", SchtasksBackend.EscapeWindowsArg(@"C:\Program Files"));
    }

    [Fact]
    public void BuildTaskRunString_NoArguments_ReturnsCommandPossiblyEscaped()
    {
        Assert.Equal("dotnet", SchtasksBackend.BuildTaskRunString("dotnet", System.Array.Empty<string>()));
    }

    [Fact]
    public void BuildTaskRunString_NoArguments_QuotesCommandWithSpaces()
    {
        Assert.Equal(@"""C:\Program Files\dotnet\dotnet.exe""",
            SchtasksBackend.BuildTaskRunString(@"C:\Program Files\dotnet\dotnet.exe", System.Array.Empty<string>()));
    }

    [Fact]
    public void BuildTaskRunString_MultipleArguments_SeparatedBySpace()
    {
        Assert.Equal("dotnet build --verbose",
            SchtasksBackend.BuildTaskRunString("dotnet", new[] { "build", "--verbose" }));
    }

    [Fact]
    public void BuildTaskRunString_ArgWithSpace_Quoted()
    {
        Assert.Equal("curl \"http://localhost/health check\"",
            SchtasksBackend.BuildTaskRunString("curl", new[] { "http://localhost/health check" }));
    }

    [Fact]
    public void BuildTaskRunString_ArgWithSpaceAndTrailingBackslash_DoubledBeforeClosingQuote()
    {
        // The canonical regression: arg has BOTH a space (forces quoting) AND a trailing
        // backslash. Without the trailing-backslash doubling rule, the backslash before the
        // closing quote would escape the quote, breaking the round-trip.
        // Input: C:\Program Files\
        // Expected: "C:\Program Files\\"  (two backslashes before closing quote = one literal)
        Assert.Equal("\"C:\\Program Files\\\\\"",
            SchtasksBackend.BuildTaskRunString(@"C:\Program Files\", System.Array.Empty<string>()));
    }
}
