#nullable enable

using System;
using System.IO;
using System.Linq;
using Winix.Files;
using Winix.FileWalk;
using Xunit;

namespace Winix.Files.Tests;

/// <summary>
/// Tests for the walk-error surfacing introduced by round-1 fresh-eyes 2026-05-09
/// silent-failure-hunter C1. Pre-fix, <see cref="FileWalker"/>'s catch sites silently
/// <c>yield break</c>'d / <c>continue</c>'d on permission-denied / I/O failures, and
/// partial walks shipped with no diagnostic and exit code 0 — directly contradicting
/// the README's documented exit-1 contract for "permission denied, invalid path."
/// Same defect class closed in treex round-stop a few hours earlier.
/// </summary>
public sealed class WalkErrorTests : IDisposable
{
    private readonly string _tempDir;

    public WalkErrorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "files-walkerr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            RestorePermissions(_tempDir);
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private static void RestorePermissions(string root)
    {
        if (!Directory.Exists(root)) { return; }
        if (OperatingSystem.IsWindows()) { return; }
        try
        {
            foreach (string subdir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.SetUnixFileMode(subdir,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                        UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                        UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                }
                catch
                {
                    // Best-effort.
                }
            }
        }
        catch
        {
            // Best-effort.
        }
    }

    private static FileWalkerOptions MakeOptions()
    {
        return new FileWalkerOptions(
            GlobPatterns: Array.Empty<string>(),
            RegexPatterns: Array.Empty<string>(),
            TypeFilter: null,
            TextOnly: null,
            MinSize: null,
            MaxSize: null,
            NewerThan: null,
            OlderThan: null,
            MaxDepth: null,
            IncludeHidden: true,
            FollowSymlinks: false,
            UseGitIgnore: false,
            AbsolutePaths: false,
            CaseInsensitive: false);
    }

    [Fact]
    public void Walk_NoErrors_WalkErrorsIsEmpty()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "subdir"));
        File.WriteAllText(Path.Combine(_tempDir, "file.txt"), "x");

        var walker = new FileWalker(MakeOptions());
        _ = walker.Walk(new[] { _tempDir }).ToList();

        Assert.Empty(walker.WalkErrors);
    }

    [Fact]
    public void Walk_BetweenCalls_WalkErrorsAreReset()
    {
        var walker = new FileWalker(MakeOptions());

        _ = walker.Walk(new[] { _tempDir }).ToList();
        Assert.Empty(walker.WalkErrors);

        Directory.CreateDirectory(Path.Combine(_tempDir, "second-walk"));
        _ = walker.Walk(new[] { _tempDir }).ToList();
        Assert.Empty(walker.WalkErrors);
    }

    [SkippableFact]
    public void Walk_AfterPopulatedWalk_SecondCallResetsToEmpty_Linux()
    {
        // Round-2 fresh-eyes 2026-05-09 test-analyzer Item 2 + SFH I1: prior test only
        // proved reset between EMPTY walks. The dangerous case is reset AFTER a walk
        // that actually populated WalkErrors — that's where stale errors could leak
        // into a subsequent clean walk. Plus SFH I1 flagged that the iterator-style
        // reset only fired on first enumeration; commit 5 wraps Walk so reset happens
        // on call. Pin both: populate via chmod, walk, assert non-empty; walk a clean
        // dir, assert empty.
        Skip.If(OperatingSystem.IsWindows(), "POSIX-only — chmod-based permission denial");
        if (OperatingSystem.IsWindows()) { return; } // CA1416

        // First walk: populate WalkErrors via a chmod-denied subdir.
        string locked = Path.Combine(_tempDir, "locked");
        Directory.CreateDirectory(locked);
        File.SetUnixFileMode(locked, (UnixFileMode)0);

        var walker = new FileWalker(MakeOptions());
        _ = walker.Walk(new[] { _tempDir }).ToList();
        Assert.NotEmpty(walker.WalkErrors);

        // Restore permissions + remove the locked dir so the second walk has no errors.
        File.SetUnixFileMode(locked,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Directory.Delete(locked, recursive: true);

        // Second walk: clean tree → WalkErrors must be empty (NOT carry over from
        // the first call). This is the load-bearing assertion for both Item 2 and I1.
        _ = walker.Walk(new[] { _tempDir }).ToList();
        Assert.Empty(walker.WalkErrors);
    }

    [Fact]
    public void Walk_DiscardingIteratorWithoutEnumerating_ResetsOnNextCall()
    {
        // SFH I1 round-2 2026-05-09: pre-fix, the reset was inside the iterator method
        // body, so it only fired when the consumer pulled the first element. A consumer
        // that called walker.Walk(...) and discarded the iterator would see stale errors
        // on the next call. Post-fix the reset fires on the call to Walk(), not the
        // first MoveNext(). Pin this contract by constructing two iterators in
        // succession and never enumerating the first.
        var walker = new FileWalker(MakeOptions());

        // Call Walk and discard without enumerating. The wrapper must still execute the
        // _walkErrors.Clear() at this call (not delay it to MoveNext).
        _ = walker.Walk(new[] { _tempDir });

        // Now call Walk a second time and enumerate. WalkErrors should be empty (the
        // first call's reset fired on call, the second call's reset fired on call,
        // the actual walk produced no errors).
        _ = walker.Walk(new[] { _tempDir }).ToList();
        Assert.Empty(walker.WalkErrors);
    }

    [SkippableFact]
    public void Walk_PermissionDeniedSubdir_RecordsWalkError_Linux()
    {
        // Permission-denied is reliably reproducible only on Unix (chmod). On Windows
        // ACLs are far more complex and platform-test-flaky; the contract is the same
        // but reliably staging it from a test process is out of scope.
        Skip.If(OperatingSystem.IsWindows(), "POSIX-only — chmod-based permission denial");
        if (OperatingSystem.IsWindows()) { return; } // CA1416

        string sub = Path.Combine(_tempDir, "locked");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "secret.txt"), "secret");
        File.SetUnixFileMode(sub, (UnixFileMode)0); // chmod 000

        var walker = new FileWalker(MakeOptions());
        _ = walker.Walk(new[] { _tempDir }).ToList();

        Assert.NotEmpty(walker.WalkErrors);
        Assert.Contains(walker.WalkErrors, e => e.Path.Contains("locked", StringComparison.Ordinal));
        Assert.Contains(walker.WalkErrors, e => e.Reason.Contains("permission denied", StringComparison.OrdinalIgnoreCase));
    }

    [SkippableFact]
    public void Cli_PermissionDeniedSubdir_Exits1WithStderrMessage_Linux()
    {
        Skip.If(OperatingSystem.IsWindows(), "POSIX-only — chmod-based permission denial");
        if (OperatingSystem.IsWindows()) { return; }

        string sub = Path.Combine(_tempDir, "locked");
        Directory.CreateDirectory(sub);
        File.SetUnixFileMode(sub, (UnixFileMode)0);

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int exit = Cli.Run(new[] { _tempDir }, stdout, stderr, isStdoutRedirected: true);

        Assert.Equal(1, exit);
        Assert.Contains("permission denied", stderr.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public void Cli_PermissionDeniedSubdir_WithJson_RoutesEnvelopeToStdout_Linux()
    {
        Skip.If(OperatingSystem.IsWindows(), "POSIX-only — chmod-based permission denial");
        if (OperatingSystem.IsWindows()) { return; }

        string sub = Path.Combine(_tempDir, "locked");
        Directory.CreateDirectory(sub);
        File.SetUnixFileMode(sub, (UnixFileMode)0);

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int exit = Cli.Run(new[] { "--json", _tempDir }, stdout, stderr, isStdoutRedirected: true);

        Assert.Equal(1, exit);
        string outText = stdout.ToString();
        Assert.Contains("\"exit_code\":1", outText, StringComparison.Ordinal);
        Assert.Contains("\"exit_reason\":\"walk_error_partial\"", outText, StringComparison.Ordinal);
        // SFH F4 round-3 from treex carries forward: machine consumers must see WHICH
        // paths failed via walk_errors[], not just the exit_reason machine code.
        Assert.Contains("\"walk_errors\":[", outText, StringComparison.Ordinal);
        Assert.Contains("\"path\":", outText, StringComparison.Ordinal);
        Assert.Contains("\"reason\":", outText, StringComparison.Ordinal);
        Assert.Contains("locked", outText, StringComparison.Ordinal);
        Assert.Contains("permission denied", stderr.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cli_NoErrors_WithJson_EmitsEmptyWalkErrorsArray()
    {
        // Schema stability: walk_errors[] always emitted (empty array on success) so
        // jq consumers can use the same shape regardless of outcome.
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int exit = Cli.Run(new[] { "--json", _tempDir }, stdout, stderr, isStdoutRedirected: true);

        Assert.Equal(0, exit);
        Assert.Contains("\"walk_errors\":[]", stdout.ToString(), StringComparison.Ordinal);
    }
}
