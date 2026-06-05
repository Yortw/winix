using Yort.ShellKit;
using Xunit;

namespace Yort.ShellKit.Tests;

public class GlobArgExpanderTests
{
    // Fake FS: dir path → entries. Keys are the exact strings the expander passes
    // ("." for the current dir; otherwise the verbatim candidate path).
    private static GlobArgExpander Fake(Dictionary<string, GlobArgExpander.FsEntry[]> fs)
        => new(dir => fs.TryGetValue(dir, out var e) ? new List<GlobArgExpander.FsEntry>(e) : new List<GlobArgExpander.FsEntry>());

    private static GlobArgExpander.FsEntry F(string name) => new(name, IsDirectory: false);
    private static GlobArgExpander.FsEntry D(string name) => new(name, IsDirectory: true);

    [Fact]
    public void NoMetachars_NotAPattern()
    {
        var x = Fake(new() { ["."] = new[] { F("a.txt") } });
        Assert.Equal(GlobExpansionKind.NotAPattern, x.Expand("a.txt").Kind);
        Assert.Equal(GlobExpansionKind.NotAPattern, x.Expand("report[1].txt").Kind); // [...] is literal
        Assert.Equal(GlobExpansionKind.NotAPattern, x.Expand("-").Kind);
    }

    [Fact]
    public void DoubleStar_Unsupported()
    {
        var x = Fake(new());
        Assert.Equal(GlobExpansionKind.UnsupportedRecursive, x.Expand("**/*.cs").Kind);
        Assert.Equal(GlobExpansionKind.UnsupportedRecursive, x.Expand("a**b").Kind);
    }

    [Fact]
    public void Star_MatchesSorted()
    {
        var x = Fake(new() { ["."] = new[] { F("b.txt"), F("a.TXT"), F("c.log") } });
        var r = x.Expand("*.txt");
        Assert.Equal(GlobExpansionKind.Expanded, r.Kind);
        Assert.Equal(new[] { "a.TXT", "b.txt" }, r.Matches); // case-insensitive match, ordinal-ci sort
    }

    [Fact]
    public void QuestionMark_ExactlyOneChar()
    {
        var x = Fake(new() { ["."] = new[] { F("a1.txt"), F("a12.txt") } });
        var r = x.Expand("a?.txt");
        Assert.Equal(new[] { "a1.txt" }, r.Matches);
    }

    [Fact]
    public void NoMatch_ReturnsNoMatch()
    {
        var x = Fake(new() { ["."] = new[] { F("a.txt") } });
        Assert.Equal(GlobExpansionKind.NoMatch, x.Expand("*.zip").Kind);
    }

    [Fact]
    public void DotfileRule_StarSkipsLeadingDot_DotPatternMatches()
    {
        var x = Fake(new() { ["."] = new[] { F(".hidden"), F("shown") } });
        Assert.Equal(new[] { "shown" }, x.Expand("*").Matches);
        Assert.Equal(new[] { ".hidden" }, x.Expand(".*").Matches);
        Assert.Equal(GlobExpansionKind.NoMatch, x.Expand("?hidden").Kind); // ? doesn't match the dot either
    }

    [Fact]
    public void FinalSegment_MatchesFilesAndDirectories()
    {
        var x = Fake(new() { ["."] = new[] { F("a.x"), D("b.x") } });
        Assert.Equal(new[] { "a.x", "b.x" }, x.Expand("*.x").Matches);
    }

    [Fact]
    public void TrailingSeparator_DirsOnly_SeparatorPreserved()
    {
        var x = Fake(new() { ["."] = new[] { F("a.x"), D("b.x") } });
        Assert.Equal(new[] { "b.x\\" }, x.Expand("*.x\\").Matches);
        Assert.Equal(new[] { "b.x/" }, x.Expand("*.x/").Matches);
    }

    [Fact]
    public void LiteralPrefix_KeptVerbatim_TypedSeparatorReused()
    {
        var x = Fake(new() { ["src"] = new[] { F("a.cs"), F("b.cs") } });
        Assert.Equal(new[] { "src\\a.cs", "src\\b.cs" }, x.Expand("src\\*.cs").Matches);
        Assert.Equal(new[] { "src/a.cs", "src/b.cs" }, x.Expand("src/*.cs").Matches);
    }

    [Fact]
    public void IntermediateWildcard_OnlyDescendsDirectories()
    {
        var x = Fake(new()
        {
            ["."] = new[] { D("one"), D("two"), F("decoy") },
            ["one"] = new[] { F("hit.log") },
            ["two"] = new[] { F("other.txt") },
        });
        Assert.Equal(new[] { "one\\hit.log" }, x.Expand("*\\hit.log").Matches);
    }

    [Fact]
    public void FinalLiteralAfterWildcard_CaseInsensitive_DiskCasingReturned()
    {
        var x = Fake(new()
        {
            ["."] = new[] { D("Proj") },
            ["Proj"] = new[] { F("README.md") },
        });
        Assert.Equal(new[] { "Proj\\README.md" }, x.Expand("*\\readme.md").Matches);
    }

    [Fact]
    public void DriveRootPrefix_KeepsRootSeparator()
    {
        var x = Fake(new() { ["C:\\"] = new[] { F("pagefile.sys"), D("Windows") } });
        Assert.Equal(new[] { "C:\\pagefile.sys", "C:\\Windows" }, x.Expand("C:\\*").Matches);
    }

    [Fact]
    public void RelativeParent_WalksLiteralPrefix()
    {
        var x = Fake(new() { [".."] = new[] { F("up.txt") } });
        Assert.Equal(new[] { "..\\up.txt" }, x.Expand("..\\*.txt").Matches);
    }

    [Fact]
    public void EnumerationFailure_IsNoMatch()
    {
        var boom = new GlobArgExpander(_ => throw new UnauthorizedAccessException("nope"));
        // The DEFAULT enumerator swallows; the seam contract is "throw = let it surface"?
        // No: the expander itself must guard, so a custom seam may throw too.
        // NOTE (review F2, explicit defer): this includes access-denied on the user-typed
        // literal prefix — it reads as "not found" downstream, same as bash. See ADR.
        Assert.Equal(GlobExpansionKind.NoMatch, boom.Expand("*.txt").Kind);
    }

    [Fact]
    public void UncSharePrefix_KeptVerbatim()
    {
        // Review F1: design advertises UNC; lock the verbatim-prefix + root-separator handling.
        var x = Fake(new() { [@"\\server\share\"] = new[] { F("a.log"), F("b.txt") } });
        Assert.Equal(new[] { @"\\server\share\a.log" }, x.Expand(@"\\server\share\*.log").Matches);
    }

    [Fact]
    public void WildcardInUncShareComponent_IsNoMatch()
    {
        // \\server\*\x → prefix "\\server\" — enumerating a server (share enumeration) is
        // not supported; the seam returning nothing/throwing yields literal passthrough.
        var x = Fake(new());
        Assert.Equal(GlobExpansionKind.NoMatch, x.Expand(@"\\server\*\x.log").Kind);
    }

    [Fact]
    public void DriveRelativePattern_IsNoMatch_LiteralPassthrough()
    {
        // Review F1: C:*.txt (drive-relative, no separator) is deliberately unsupported —
        // the segment text contains "C:" so no filename can ever match; the literal passes
        // through and the tool reports its normal not-found. Documented known limitation.
        var x = Fake(new() { ["."] = new[] { F("a.txt") } });
        Assert.Equal(GlobExpansionKind.NoMatch, x.Expand("C:*.txt").Kind);
    }

    [Fact]
    public void SurrogatePairFilename_StarMatches_NamePreserved()
    {
        // Review F5: non-BMP filename must match * and come back byte-identical.
        string name = "a" + char.ConvertFromUtf32(0x1F600) + ".txt";
        var x = Fake(new() { ["."] = new[] { F(name) } });
        Assert.Equal(new[] { name }, x.Expand("a*.txt").Matches);
    }
}
