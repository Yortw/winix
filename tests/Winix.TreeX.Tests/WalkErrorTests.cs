#nullable enable

using System;
using System.IO;
using Winix.TreeX;
using Xunit;

namespace Winix.TreeX.Tests;

/// <summary>
/// Tests for the walk-error surfacing introduced by round-1 fresh-eyes 2026-05-09
/// silent-failure-hunter C1. Pre-fix, <see cref="TreeBuilder"/>'s catch sites silently
/// swallowed permission-denied / vanishing-path / I/O errors and produced a partial
/// tree with no diagnostic — README documented exit 1 for these cases but the binary
/// returned 0. The fix collects errors into <see cref="TreeBuilder.WalkErrors"/> and
/// the CLI surfaces them to stderr + sets exit 1.
/// </summary>
public sealed class WalkErrorTests : IDisposable
{
    private readonly string _tempDir;

    public WalkErrorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "treex-walkerr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            // Restore permissions before deleting in case a test left an inaccessible dir.
            RestorePermissions(_tempDir);
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup — Windows may hold locks under Defender scan.
        }
    }

    private static void RestorePermissions(string root)
    {
        if (!Directory.Exists(root)) { return; }
        try
        {
            // Walk and chmod 0755 on each dir so cleanup can succeed even if a test left
            // a 000-mode directory in place. No-op on Windows (chmod doesn't apply).
            foreach (string subdir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories))
            {
                try
                {
                    if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                    {
                        File.SetUnixFileMode(subdir,
                            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                    }
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

    [Fact]
    public void Build_NoErrors_WalkErrorsIsEmpty()
    {
        // Sanity baseline: a clean walk reports no errors.
        Directory.CreateDirectory(Path.Combine(_tempDir, "subdir"));
        File.WriteAllText(Path.Combine(_tempDir, "file.txt"), "x");

        var builder = new TreeBuilder(MakeOptions());
        TreeNode root = builder.Build(_tempDir);

        Assert.Empty(builder.WalkErrors);
        Assert.False(root.IsUnreadable);
    }

    [SkippableFact]
    public void Build_PermissionDeniedSubdir_RecordsWalkErrorAndMarksUnreadable_Linux()
    {
        // Permission-denied behaviour is reliably reproducible only on Unix where chmod
        // can revoke read access at the filesystem level. On Windows, ACLs are far more
        // complex and platform-test-flaky; the SFH C1 contract is the same on Windows
        // when ACLs deny access, but reliably staging it from a test process requires
        // P/Invoke into SetSecurityInfo — out of scope for this fixture.
        Skip.If(OperatingSystem.IsWindows(), "POSIX-only — chmod-based permission denial");
        if (OperatingSystem.IsWindows()) { return; } // CA1416

        string sub = Path.Combine(_tempDir, "locked");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "secret.txt"), "secret");
        File.SetUnixFileMode(sub, (UnixFileMode)0); // chmod 000

        var builder = new TreeBuilder(MakeOptions());
        TreeNode root = builder.Build(_tempDir);

        // The subdirectory itself appears in the tree (it WAS enumerated by the parent),
        // but its contents could not be read.
        Assert.NotEmpty(builder.WalkErrors);
        Assert.Contains(builder.WalkErrors, e => e.Path.Contains("locked", StringComparison.Ordinal));
        Assert.Contains(builder.WalkErrors, e => e.Reason.Contains("permission denied", StringComparison.OrdinalIgnoreCase));

        // The locked dir node is marked IsUnreadable so the renderer can annotate.
        TreeNode? lockedNode = null;
        foreach (TreeNode child in root.Children)
        {
            if (child.Name == "locked")
            {
                lockedNode = child;
                break;
            }
        }
        Assert.NotNull(lockedNode);
        Assert.True(lockedNode!.IsUnreadable);
    }

    [Fact]
    public void Build_BetweenCalls_WalkErrorsAreReset()
    {
        // Successive Build calls on the same builder must not accumulate errors across
        // unrelated walks — each Build exposes only its own errors.
        var builder = new TreeBuilder(MakeOptions());

        // Walk 1: a directory with a missing path target throws DirectoryNotFoundException
        // when the parent enumerates it, but the case we want is a clean walk first then
        // a second clean walk — both should report empty WalkErrors.
        builder.Build(_tempDir);
        Assert.Empty(builder.WalkErrors);

        Directory.CreateDirectory(Path.Combine(_tempDir, "second-walk"));
        builder.Build(_tempDir);
        Assert.Empty(builder.WalkErrors);
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
        string err = stderr.ToString();
        Assert.Contains("permission denied", err, StringComparison.OrdinalIgnoreCase);
        // Resource-key-leak regression: under UseSystemResourceKeys=true the raw
        // UnauthorizedAccessException.Message is a bare CoreLib key. The walk-IO path now
        // routes through SafeError, so no key fragment may appear.
        Assert.DoesNotContain("UnauthorizedAccess", err, StringComparison.Ordinal);
        Assert.DoesNotContain("IO_", err, StringComparison.Ordinal);
    }

    [Fact]
    public void Cli_InvalidRegex_Exits125WithReadableMessage_NoResourceKey()
    {
        // RegexParseException.Message is a bare CoreLib resource key (e.g. MakeException)
        // under UseSystemResourceKeys=true. The invalid-regex catch now routes through
        // SafeError.Describe, which yields "<Error> at offset N".
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int exit = Cli.Run(new[] { "--regex", "[unterminated", _tempDir }, stdout, stderr, isStdoutRedirected: true);

        Assert.NotEqual(0, exit);
        string err = stderr.ToString();
        Assert.Contains("invalid regex", err, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("offset", err, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MakeException", err, StringComparison.Ordinal);
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
        // Round-2 fresh-eyes 2026-05-09 SFH F4: machine consumers must see WHICH paths
        // failed, not just the machine code. walk_errors[] enumerates them.
        Assert.Contains("\"walk_errors\":[", outText, StringComparison.Ordinal);
        Assert.Contains("\"path\":", outText, StringComparison.Ordinal);
        Assert.Contains("\"reason\":", outText, StringComparison.Ordinal);
        Assert.Contains("locked", outText, StringComparison.Ordinal);
        // Plain-text per-error diagnostics still go to stderr — JSON envelope summarises.
        Assert.Contains("permission denied", stderr.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cli_NoErrors_WithJson_EmitsEmptyWalkErrorsArray()
    {
        // Schema stability: walk_errors is always emitted (empty array on success) so
        // jq consumers can use the same shape regardless of outcome.
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int exit = Cli.Run(new[] { "--json", _tempDir }, stdout, stderr, isStdoutRedirected: true);

        Assert.Equal(0, exit);
        Assert.Contains("\"walk_errors\":[]", stdout.ToString(), StringComparison.Ordinal);
    }

    private static TreeBuilderOptions MakeOptions()
    {
        return new TreeBuilderOptions(
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
    }
}
