#nullable enable

using Winix.Less;
using Xunit;

namespace Winix.Less.Tests;

public class SearchEngineTests
{
    private readonly string[] _lines = new[]
    {
        "First line of text",
        "Second line with ERROR here",
        "Third line is plain",
        "Fourth line has error too",
        "Fifth and final line"
    };

    // 1. FindNext returns line index when a match exists
    [Fact]
    public void FindNext_MatchExists_ReturnsLineIndex()
    {
        var engine = new SearchEngine();
        int? result = engine.FindNext(_lines, "ERROR", 0);
        Assert.Equal(1, result);
    }

    // 2. FindNext returns null when pattern is not found
    [Fact]
    public void FindNext_NoMatch_ReturnsNull()
    {
        var engine = new SearchEngine();
        int? result = engine.FindNext(_lines, "NOTFOUND", 0);
        Assert.Null(result);
    }

    // 3. FindNext wraps around from end back to beginning
    [Fact]
    public void FindNext_WrapsAround()
    {
        var engine = new SearchEngine();
        // startLine 3 — "Fourth line has error too" (lowercase, won't match "ERROR")
        // Lines 3 and 4 have no "ERROR"; should wrap and find line 1
        int? result = engine.FindNext(_lines, "ERROR", 3);
        Assert.Equal(1, result);
    }

    // 4. FindPrevious returns the previous match before the start line
    [Fact]
    public void FindPrevious_MatchExists_ReturnsLineIndex()
    {
        var engine = new SearchEngine();
        // Searching backward from line 4 for lowercase "error" — line 3 has it
        int? result = engine.FindPrevious(_lines, "error", 4);
        Assert.Equal(3, result);
    }

    // 5. FindPrevious wraps around from the beginning to the end
    [Fact]
    public void FindPrevious_WrapsAround()
    {
        var engine = new SearchEngine();
        // Searching backward from line 0 for "error" — nothing before 0, wraps to find line 3
        int? result = engine.FindPrevious(_lines, "error", 0);
        Assert.Equal(3, result);
    }

    // 6. FindNext is case-sensitive by default
    [Fact]
    public void FindNext_CaseSensitive_MatchesExact()
    {
        var engine = new SearchEngine();

        // "ERROR" (uppercase) → line 1
        int? upperResult = engine.FindNext(_lines, "ERROR", 0);
        Assert.Equal(1, upperResult);

        // "error" (lowercase) → line 3
        int? lowerResult = engine.FindNext(_lines, "error", 0);
        Assert.Equal(3, lowerResult);
    }

    // 7. FindNext with IgnoreCase matches both cases
    [Fact]
    public void FindNext_IgnoreCase_MatchesBothCases()
    {
        var engine = new SearchEngine { IgnoreCase = true };
        // "error" with IgnoreCase → should match line 1 first (has "ERROR")
        int? result = engine.FindNext(_lines, "error", 0);
        Assert.Equal(1, result);
    }

    // 8. SmartCase with all-lowercase pattern behaves like IgnoreCase
    [Fact]
    public void FindNext_SmartCase_LowercasePatternIgnoresCase()
    {
        var engine = new SearchEngine { SmartCase = true };
        // All-lowercase "error" → smart case treats as case-insensitive → line 1 ("ERROR")
        int? result = engine.FindNext(_lines, "error", 0);
        Assert.Equal(1, result);
    }

    // 9. SmartCase with an uppercase letter in pattern is case-sensitive
    [Fact]
    public void FindNext_SmartCase_UppercasePatternIsCaseSensitive()
    {
        var engine = new SearchEngine { SmartCase = true };

        // "ERROR" has uppercase → case-sensitive → line 1
        int? errorResult = engine.FindNext(_lines, "ERROR", 0);
        Assert.Equal(1, errorResult);

        // "Error" has uppercase but no exact match in any line → null
        int? mixedResult = engine.FindNext(_lines, "Error", 0);
        Assert.Null(mixedResult);
    }

    // 10. FindNext strips ANSI sequences before matching
    [Fact]
    public void FindNext_WithAnsiInLines_MatchesVisibleText()
    {
        var linesWithAnsi = new[]
        {
            "Plain line",
            "\x1b[1mSecond line with \x1b[31mERROR\x1b[0m here",
            "Third plain line"
        };

        var engine = new SearchEngine();
        int? result = engine.FindNext(linesWithAnsi, "ERROR", 0);
        Assert.Equal(1, result);
    }

    // 11. CurrentPattern is null initially and is updated after each search
    [Fact]
    public void CurrentPattern_TracksLastSearch()
    {
        var engine = new SearchEngine();
        Assert.Null(engine.CurrentPattern);

        engine.FindNext(_lines, "ERROR", 0);
        Assert.Equal("ERROR", engine.CurrentPattern);

        engine.FindPrevious(_lines, "error", 4);
        Assert.Equal("error", engine.CurrentPattern);
    }
}
