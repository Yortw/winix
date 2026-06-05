using Yort.ShellKit;
using Xunit;

namespace Yort.ShellKit.Tests;

/// <summary>
/// Collection that serialises real-FS glob tests: they change the process CWD via
/// <see cref="Directory.SetCurrentDirectory"/>, which is process-wide state. Parallel
/// execution with any other CWD-sensitive test would cause non-deterministic failures.
/// Only tests in this file are placed here — assembly-wide parallelization is NOT disabled.
/// </summary>
[CollectionDefinition("RealFsCwd", DisableParallelization = true)]
public class RealFsCwdCollection
{
}

[Collection("RealFsCwd")]
public class GlobArgExpanderRealFsTests_Windows : IDisposable
{
    private readonly string _root;
    private readonly string _originalCwd;

    public GlobArgExpanderRealFsTests_Windows()
    {
        _root = Path.Combine(Path.GetTempPath(), "shellkit-glob-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _originalCwd = Directory.GetCurrentDirectory();
    }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_originalCwd);
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [SkippableFact]
    public void RealFs_StarPattern_MatchesIncludingHiddenAttribute()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "real-FS glob semantics under test are Windows-only");
        if (!OperatingSystem.IsWindows()) { return; } // deliberate redundancy for CA1416

        File.WriteAllText(Path.Combine(_root, "a.txt"), "a");
        File.WriteAllText(Path.Combine(_root, "b.txt"), "b");
        File.WriteAllText(Path.Combine(_root, "c.log"), "c");
        string hidden = Path.Combine(_root, "h.txt");
        File.WriteAllText(hidden, "h");
        File.SetAttributes(hidden, FileAttributes.Hidden);
        File.WriteAllText(Path.Combine(_root, ".dot.txt"), "d");
        Directory.SetCurrentDirectory(_root);

        var r = new GlobArgExpander().Expand("*.txt");

        Assert.Equal(GlobExpansionKind.Expanded, r.Kind);
        // Hidden-ATTRIBUTE file matches (bash parity); leading-DOT file does not.
        Assert.Equal(new[] { "a.txt", "b.txt", "h.txt" }, r.Matches);
    }

    [SkippableFact]
    public void RealFs_IntermediateWildcard_And_NoMatch()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "real-FS glob semantics under test are Windows-only");
        if (!OperatingSystem.IsWindows()) { return; } // deliberate redundancy for CA1416

        Directory.CreateDirectory(Path.Combine(_root, "one"));
        Directory.CreateDirectory(Path.Combine(_root, "two"));
        File.WriteAllText(Path.Combine(_root, "one", "hit.log"), "x");
        Directory.SetCurrentDirectory(_root);

        var hit = new GlobArgExpander().Expand("*\\hit.log");
        Assert.Equal(new[] { "one\\hit.log" }, hit.Matches);

        Assert.Equal(GlobExpansionKind.NoMatch, new GlobArgExpander().Expand("*.zip").Kind);
        // Nonexistent literal prefix → enumeration failure → NoMatch, not an exception.
        Assert.Equal(GlobExpansionKind.NoMatch, new GlobArgExpander().Expand("missing-dir\\*.txt").Kind);
    }
}
