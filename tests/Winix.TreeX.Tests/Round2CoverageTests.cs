#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Winix.TreeX;
using Xunit;

namespace Winix.TreeX.Tests;

/// <summary>
/// Round-2 fresh-eyes 2026-05-09 test-analyzer coverage gaps:
/// <list type="bullet">
///   <item>I4 — symlink cycle detection (POSIX-only fixture).</item>
///   <item>I5 — <c>--gitignore</c> warning when no roots are inside a git repo.</item>
///   <item>I6 — Windows <c>IsExecutable</c> extension whitelist (data-driven theory).</item>
///   <item>I7 — <see cref="TreeBuilder.IsMatchSafe"/> RegexMatchTimeoutException swallow.</item>
/// </list>
/// </summary>
public sealed class Round2CoverageTests : IDisposable
{
    private readonly string _tempDir;

    public Round2CoverageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "treex-r2cov-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Best-effort.
        }
    }

    // ── I7: IsMatchSafe RegexMatchTimeoutException swallow ────────────────────────

    [Fact]
    public void IsMatchSafe_RegexTimesOut_TreatsAsNonMatch()
    {
        // Construct a pathological pattern + input that catastrophically backtracks on
        // the standard engine, with a 1-tick timeout to force RegexMatchTimeoutException.
        // This pins the swallow path: the exception is caught, the pattern is treated
        // as a non-match, the helper continues to evaluate other regexes (none here).
        var pathological = new Regex(
            "(a+)+$",
            RegexOptions.None,
            TimeSpan.FromTicks(1));

        // Long sequence of 'a's followed by a non-matching char triggers the worst case.
        string input = new string('a', 30) + "!";

        bool result = TreeBuilder.IsMatchSafe(new[] { pathological }, input);

        Assert.False(result);
    }

    [Fact]
    public void IsMatchSafe_OneTimesOutOneMatches_ReturnsTrue()
    {
        // Even when one regex times out, a subsequent regex that matches still wins.
        // Pin that the swallow does not short-circuit the loop.
        var pathological = new Regex("(a+)+$", RegexOptions.None, TimeSpan.FromTicks(1));
        var simple = new Regex("hello", RegexOptions.None);

        bool result = TreeBuilder.IsMatchSafe(
            new[] { pathological, simple },
            "hello world");

        Assert.True(result);
    }

    [Fact]
    public void IsMatchSafe_NoRegexes_ReturnsFalse()
    {
        // Defensive: empty regex array → false (no possible match).
        bool result = TreeBuilder.IsMatchSafe(Array.Empty<Regex>(), "anything");

        Assert.False(result);
    }

    // ── I5: --gitignore warning when not in a git repo ─────────────────────────────

    [Fact]
    public void Run_GitignoreOutsideRepo_WarnsButContinues()
    {
        // Pre-fix this contract was unobserved by any test. Setup: tempdir not inside a
        // git repository (the harness creates fresh temp roots). Pass --gitignore and
        // assert the documented warning lands on stderr; exit 0 so non-fatal.
        File.WriteAllText(Path.Combine(_tempDir, "regular.txt"), "x");
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        int exit = Cli.Run(
            new[] { "--gitignore", _tempDir },
            stdout, stderr, isStdoutRedirected: true);

        Assert.Equal(0, exit);
        Assert.Contains(
            "--gitignore specified but git not found on PATH or no roots are inside a git repository",
            stderr.ToString(),
            StringComparison.Ordinal);
    }

    // ── I6: Windows IsExecutable extension whitelist ──────────────────────────────

    [SkippableTheory]
    [InlineData("foo.exe", true)]
    [InlineData("Foo.EXE", true)]      // case-insensitive
    [InlineData("script.cmd", true)]
    [InlineData("script.bat", true)]
    [InlineData("script.ps1", true)]
    [InlineData("legacy.com", true)]
    [InlineData("data.txt", false)]
    [InlineData("config.json", false)]
    [InlineData("library.dll", false)]  // .dll is NOT in the executable list
    [InlineData("noext", false)]         // no extension at all
    public void IsExecutable_WindowsExtensions_DetectedCaseInsensitive(string fileName, bool expected)
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows-only — Unix uses UnixFileMode.UserExecute");
        if (!OperatingSystem.IsWindows()) { return; } // CA1416

        // Indirect: build a tree containing a file with the given name, read back the
        // node's IsExecutable. The extension whitelist is data-driven; this theory pins
        // each entry plus a casing check and the rejected-extensions cases.
        File.WriteAllText(Path.Combine(_tempDir, fileName), "x");

        var options = new TreeBuilderOptions(
            GlobPatterns: Array.Empty<string>(),
            RegexPatterns: Array.Empty<string>(),
            TypeFilter: null,
            MinSize: null,
            MaxSize: null,
            NewerThan: null,
            OlderThan: null,
            MaxDepth: null,
            IncludeHidden: true,
            UseGitIgnore: false,
            CaseInsensitive: false,
            ComputeSizes: false,
            Sort: SortMode.Name);

        var builder = new TreeBuilder(options);
        TreeNode root = builder.Build(_tempDir);

        TreeNode? fileNode = root.Children.FirstOrDefault(c => c.Name == fileName);
        Assert.NotNull(fileNode);
        Assert.Equal(expected, fileNode!.IsExecutable);
    }

    // ── I4: symlink cycle detection (POSIX-only) ───────────────────────────────────

    [SkippableFact]
    public void Build_SymlinkCycleToAncestor_DoesNotRecurseInfinitely()
    {
        Skip.If(OperatingSystem.IsWindows(), "Windows symlink creation requires elevated privileges or developer mode");
        if (OperatingSystem.IsWindows()) { return; } // CA1416

        // Fixture: dir A containing a symlink 'loop' that points back to A. Without
        // cycle detection in TreeBuilder, BuildChildren would recurse infinitely until
        // StackOverflowException. With detection, the symlink target's real path is
        // already in visitedDirs and the recursion is short-circuited.
        string a = Path.Combine(_tempDir, "A");
        Directory.CreateDirectory(a);
        File.WriteAllText(Path.Combine(a, "data.txt"), "data");

        string loopLink = Path.Combine(a, "loop");
        try
        {
            Directory.CreateSymbolicLink(loopLink, a);
        }
        catch (UnauthorizedAccessException)
        {
            Skip.If(true, "Test environment does not permit symlink creation");
            return;
        }
        catch (IOException)
        {
            Skip.If(true, "Test environment does not permit symlink creation");
            return;
        }

        var options = new TreeBuilderOptions(
            GlobPatterns: Array.Empty<string>(),
            RegexPatterns: Array.Empty<string>(),
            TypeFilter: null,
            MinSize: null,
            MaxSize: null,
            NewerThan: null,
            OlderThan: null,
            MaxDepth: null,
            IncludeHidden: true,
            UseGitIgnore: false,
            CaseInsensitive: false,
            ComputeSizes: false,
            Sort: SortMode.Name);

        var builder = new TreeBuilder(options);

        // Termination is the contract; if cycle detection broke, this Build call would
        // recurse forever and either StackOverflow or run for a very long time.
        TreeNode root = builder.Build(a);

        // Sanity: the link is present in the tree (it was enumerated), but its
        // contents are not re-walked (the cycle is short-circuited).
        TreeNode? linkNode = root.Children.FirstOrDefault(c => c.Name == "loop");
        Assert.NotNull(linkNode);
        Assert.Empty(linkNode!.Children);
    }
}
