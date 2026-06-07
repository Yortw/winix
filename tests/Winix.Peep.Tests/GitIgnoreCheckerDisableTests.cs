using System.IO;
using Winix.Peep;
using Xunit;

namespace Winix.Peep.Tests;

/// <summary>
/// Pins the disable-on-failure contract for <see cref="GitIgnoreChecker"/>: once a
/// git invocation times out or fails (broken install, hung credential helper, AV
/// locking .pack files, etc.), all subsequent calls short-circuit to false rather
/// than re-spawning git children. Without this, FileSystemWatcher would pile up
/// orphan git processes one per file event under a hung-git environment.
/// </summary>
// R4 TA C1: shares process-global static state with GitIgnoreCheckerTests;
// merged into PeepProcessGlobals 2026-06-07 (CWD-flip flake) — see that class for detail.
[Collection("PeepProcessGlobals")]
public class GitIgnoreCheckerDisableTests : IDisposable
{
    private readonly TextWriter _originalWriter;

    public GitIgnoreCheckerDisableTests()
    {
        _originalWriter = GitIgnoreChecker.FailureWriter;
        GitIgnoreChecker.ResetForTests();
        // Default to a discard writer so tests that don't explicitly capture warnings
        // don't pollute the test runner's stderr.
        GitIgnoreChecker.FailureWriter = TextWriter.Null;
    }

    public void Dispose()
    {
        GitIgnoreChecker.FailureWriter = _originalWriter;
        GitIgnoreChecker.ResetForTests();
    }

    [Fact]
    public void DisableGit_SetsDisabledFlag()
    {
        Assert.False(GitIgnoreChecker.IsDisabledForTests);
        GitIgnoreChecker.DisableGit("test reason");
        Assert.True(GitIgnoreChecker.IsDisabledForTests);
    }

    [Fact]
    public void DisableGit_EmitsWarningToFailureWriterOnce()
    {
        using var capture = new StringWriter();
        GitIgnoreChecker.FailureWriter = capture;

        GitIgnoreChecker.DisableGit("test reason 1");
        GitIgnoreChecker.DisableGit("test reason 2");
        GitIgnoreChecker.DisableGit("test reason 3");

        string output = capture.ToString();
        // Exactly one warning line should be emitted, regardless of repeated calls.
        Assert.Contains("test reason 1", output);
        Assert.DoesNotContain("test reason 2", output);
        Assert.DoesNotContain("test reason 3", output);

        // Verify it's exactly one line (one trailing newline).
        Assert.Equal(1, output.Split('\n').Count(l => l.Contains("peep: warning:")));
    }

    [Fact]
    public void IsGitRepo_AfterDisable_ShortCircuitsToFalse()
    {
        GitIgnoreChecker.DisableGit("disabled for test");

        // Even if the working directory IS a git repo (the Winix repo itself), the
        // disabled flag must short-circuit before invoking git. Otherwise an environment
        // that hung once would keep hanging on every subsequent FSW event.
        Assert.False(GitIgnoreChecker.IsGitRepo());
    }

    [Fact]
    public void IsIgnored_AfterDisable_ShortCircuitsToFalse()
    {
        GitIgnoreChecker.DisableGit("disabled for test");

        // Use a unique path so the cache doesn't already have an entry — we want to
        // verify the short-circuit kicks in BEFORE the cache factory invokes git.
        string unique = $"test-{Guid.NewGuid():N}.txt";
        Assert.False(GitIgnoreChecker.IsIgnored(unique));
    }

    [Fact]
    public void ResetForTests_ClearsDisabledFlagAndAllowsWarningAgain()
    {
        using var capture1 = new StringWriter();
        GitIgnoreChecker.FailureWriter = capture1;
        GitIgnoreChecker.DisableGit("first warning");
        Assert.True(GitIgnoreChecker.IsDisabledForTests);
        Assert.Contains("first warning", capture1.ToString());

        GitIgnoreChecker.ResetForTests();
        Assert.False(GitIgnoreChecker.IsDisabledForTests);

        using var capture2 = new StringWriter();
        GitIgnoreChecker.FailureWriter = capture2;
        GitIgnoreChecker.DisableGit("second warning");
        Assert.Contains("second warning", capture2.ToString());
    }

    [Fact]
    public void DisableGit_FailureWriterThrows_DoesNotPropagate()
    {
        // Diagnostic strictly weaker than production: a failing FailureWriter (e.g.
        // stderr is closed) must not propagate out and mask the original git failure.
        GitIgnoreChecker.FailureWriter = new ThrowingWriter();

        // Should not throw despite the writer faulting.
        var ex = Record.Exception(() => GitIgnoreChecker.DisableGit("test"));
        Assert.Null(ex);
        Assert.True(GitIgnoreChecker.IsDisabledForTests);
    }

    private sealed class ThrowingWriter : TextWriter
    {
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
        public override void WriteLine(string? value) => throw new IOException("synthetic");
        public override void Write(string? value) => throw new IOException("synthetic");
        public override void Write(char value) => throw new IOException("synthetic");
    }
}
