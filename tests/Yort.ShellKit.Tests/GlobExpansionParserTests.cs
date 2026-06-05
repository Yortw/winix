using Yort.ShellKit;
using Xunit;

namespace Yort.ShellKit.Tests;

// Serialised with StandardFlagsBrokenPipeTests — that test intentionally installs a
// disposed Console.Out as a programmer-bug detector; without serialisation, a Help test
// races on the shared Console.Out and gets ObjectDisposedException.
[Collection("ConsoleOutput")]
public class GlobExpansionParserTests
{
    private static readonly Dictionary<string, GlobArgExpander.FsEntry[]> Fs = new()
    {
        ["."] = new GlobArgExpander.FsEntry[]
        {
            new("a.txt", false), new("b.txt", false), new("c.log", false),
        },
    };

    // raw == null → simulate "raw line unavailable" (provider returns null).
    private static CommandLineParser NewParser(string? raw, bool windows = true, int skipFirst = 0)
    {
        var p = new CommandLineParser("tool", "1.0").StandardFlags().ExpandGlobPositionals(skipFirst);
        p.GlobWindowsGateOverride = () => windows;
        p.GlobRawCommandLineProvider = () => raw;
        p.GlobExpanderOverride = new GlobArgExpander(
            dir => Fs.TryGetValue(dir, out var e) ? new List<GlobArgExpander.FsEntry>(e) : new List<GlobArgExpander.FsEntry>());
        return p;
    }

    [Fact]
    public void NotOptedIn_PatternUntouched()
    {
        var p = new CommandLineParser("tool", "1.0").StandardFlags();
        var r = p.Parse(new[] { "*.txt" });
        Assert.Equal(new[] { "*.txt" }, r.Positionals);
    }

    [Fact]
    public void OptedIn_NonWindows_Untouched()
    {
        var r = NewParser("\"t.exe\" *.txt", windows: false).Parse(new[] { "*.txt" });
        Assert.Equal(new[] { "*.txt" }, r.Positionals);
    }

    [Fact]
    public void UnquotedPattern_ExpandsSortedInPlace()
    {
        var r = NewParser("\"t.exe\" first *.txt last").Parse(new[] { "first", "*.txt", "last" });
        Assert.Equal(new[] { "first", "a.txt", "b.txt", "last" }, r.Positionals);
    }

    [Fact]
    public void QuotedPattern_NotExpanded()
    {
        var r = NewParser("\"t.exe\" \"*.txt\"").Parse(new[] { "*.txt" });
        Assert.Equal(new[] { "*.txt" }, r.Positionals);
    }

    [Fact]
    public void RawLineUnavailable_FailsOpen_Expands()
    {
        var r = NewParser(raw: null).Parse(new[] { "*.txt" });
        Assert.Equal(new[] { "a.txt", "b.txt" }, r.Positionals);
    }

    [Fact]
    public void RawLineMisaligned_FailsOpen_Expands()
    {
        // Raw has an extra token (dotnet-style host) → count mismatch → expand everything.
        var r = NewParser("\"dotnet.exe\" tool.dll \"*.txt\"").Parse(new[] { "*.txt" });
        Assert.Equal(new[] { "a.txt", "b.txt" }, r.Positionals);
    }

    [Fact]
    public void NoMatch_LiteralPassthrough()
    {
        var r = NewParser("\"t.exe\" *.zip").Parse(new[] { "*.zip" });
        Assert.Equal(new[] { "*.zip" }, r.Positionals);
        Assert.False(r.HasErrors);
    }

    [Fact]
    public void DoubleStar_UsageError_LiteralKept()
    {
        var r = NewParser("\"t.exe\" **/*.txt").Parse(new[] { "**/*.txt" });
        Assert.Equal(new[] { "**/*.txt" }, r.Positionals);
        Assert.True(r.HasErrors);
        Assert.Contains(r.Errors, e => e.Contains("**", StringComparison.Ordinal));
    }

    [Fact]
    public void AfterDoubleDash_StillExpanded()
    {
        var r = NewParser("\"t.exe\" -- *.txt").Parse(new[] { "--", "*.txt" });
        Assert.Equal(new[] { "a.txt", "b.txt" }, r.Positionals);
    }

    [Fact]
    public void SkipFirst_LeavesVerbUntouched()
    {
        // Subcommand-style: positional 0 is a verb (or a cron field!) — never expanded.
        var r = NewParser("\"t.exe\" * *.txt", skipFirst: 1).Parse(new[] { "*", "*.txt" });
        Assert.Equal("*", r.Positionals[0]);
        Assert.Equal(new[] { "*", "a.txt", "b.txt" }, r.Positionals);
    }

    [Fact]
    public void CommandMode_NeverExpands()
    {
        var p = new CommandLineParser("tool", "1.0").StandardFlags().CommandMode().ExpandGlobPositionals();
        p.GlobWindowsGateOverride = () => true;
        p.GlobRawCommandLineProvider = () => "\"t.exe\" child *.txt";
        var r = p.Parse(new[] { "child", "*.txt" });
        Assert.Equal(new[] { "child", "*.txt" }, r.Command);
    }

    [Fact]
    public void HelpRequested_SkipsExpansion()
    {
        int calls = 0;
        var p = new CommandLineParser("tool", "1.0").StandardFlags().ExpandGlobPositionals();
        p.GlobWindowsGateOverride = () => true;
        p.GlobRawCommandLineProvider = () => "\"t.exe\" --help *.txt";
        p.GlobExpanderOverride = new GlobArgExpander(dir => { calls++; return new List<GlobArgExpander.FsEntry>(); });
        var r = p.Parse(new[] { "--help", "*.txt" });
        Assert.True(r.IsHandled);
        Assert.Equal(0, calls); // no FS work when help/version/describe short-circuits
    }

    [Fact]
    public void ExpandAfterParse_Throws()
    {
        var p = new CommandLineParser("tool", "1.0").StandardFlags();
        p.Parse(Array.Empty<string>());
        Assert.Throws<InvalidOperationException>(() => p.ExpandGlobPositionals());
    }

    [Fact]
    public void TwoPatterns_PerPatternSort_SplicedInPatternOrder()
    {
        // Review F6: each pattern's matches are sorted independently and spliced at that
        // pattern's argv position — NOT one globally merged sort. *.log first must yield
        // its match BEFORE the *.txt matches, even though a global sort would interleave.
        var r = NewParser("\"t.exe\" *.log *.txt").Parse(new[] { "*.log", "*.txt" });
        Assert.Equal(new[] { "c.log", "a.txt", "b.txt" }, r.Positionals);
    }

    [Fact]
    public void GenerateHelp_OptedIn_IncludesWildcardsSection()
    {
        var p = new CommandLineParser("tool", "1.0").StandardFlags().ExpandGlobPositionals();
        string help = p.GenerateHelp();
        Assert.Contains("Wildcards (Windows):", help, StringComparison.Ordinal);
        Assert.Contains("'**' is not supported", help, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateHelp_NotOptedIn_OmitsWildcardsSection()
    {
        var p = new CommandLineParser("tool", "1.0").StandardFlags();
        Assert.DoesNotContain("Wildcards (Windows):", p.GenerateHelp(), StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateDescribe_OptedIn_IncludesGlobExpansion()
    {
        var p = new CommandLineParser("tool", "1.0").StandardFlags().ExpandGlobPositionals(skipFirst: 1);
        string json = p.GenerateDescribe();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var glob = doc.RootElement.GetProperty("glob_expansion");
        Assert.True(glob.GetProperty("positionals").GetBoolean());
        Assert.Equal(1, glob.GetProperty("skip_first").GetInt32());
        Assert.True(glob.GetProperty("windows_only").GetBoolean());
        Assert.Equal("literal passthrough", glob.GetProperty("no_match").GetString());
    }

    [Fact]
    public void GenerateDescribe_NotOptedIn_OmitsGlobExpansion()
    {
        var p = new CommandLineParser("tool", "1.0").StandardFlags();
        using var doc = System.Text.Json.JsonDocument.Parse(p.GenerateDescribe());
        Assert.False(doc.RootElement.TryGetProperty("glob_expansion", out _));
    }
}
