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
