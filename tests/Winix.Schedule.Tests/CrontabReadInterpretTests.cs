#nullable enable

using Xunit;

namespace Winix.Schedule.Tests;

/// <summary>
/// R4 regression pins for <c>CrontabBackend.InterpretReadResult</c>. Pre-fix, ReadCrontab
/// collapsed every non-zero <c>crontab -l</c> exit to an empty string, so a real failure
/// mode (cron.deny, PAM/LDAP error, locked spool) was indistinguishable from "no crontab
/// yet". The next WriteCrontab then overwrote the user's actual crontab using an empty
/// baseline — silent data loss reported as "Created task X."
///
/// The fix splits "legitimate empty (no crontab for $USER)" from "real failure" by
/// inspecting stderr. These tests pin both branches and the diagnostic-message contract.
/// </summary>
public sealed class CrontabReadInterpretTests
{
    [Fact]
    public void ExitZero_ReturnsStdoutVerbatim()
    {
        string stdout = "0 2 * * * /usr/bin/backup\n";
        string result = CrontabBackend.InterpretReadResult(0, stdout, "");
        Assert.Equal(stdout, result);
    }

    [Fact]
    public void ExitZero_EmptyStdout_ReturnsEmpty()
    {
        string result = CrontabBackend.InterpretReadResult(0, "", "");
        Assert.Equal("", result);
    }

    [Theory]
    [InlineData("no crontab for troy")]
    [InlineData("no crontab for troy\n")]
    [InlineData("crontab: no crontab for troy")]
    [InlineData("No crontab for troy")]
    [InlineData("NO CRONTAB FOR root")]
    public void NonZeroExit_NoCrontabForUser_ReturnsEmpty(string stderr)
    {
        string result = CrontabBackend.InterpretReadResult(1, "", stderr);
        Assert.Equal("", result);
    }

    [Fact]
    public void NonZeroExit_CronDeny_ThrowsWithStderrInMessage()
    {
        string stderr = "You (troy) are not allowed to use this program (cron)";
        var ex = Assert.Throws<CrontabBackend.CrontabUnavailableException>(() =>
            CrontabBackend.InterpretReadResult(1, "", stderr));
        Assert.Contains(stderr, ex.Message);
    }

    [Fact]
    public void NonZeroExit_PamFailure_ThrowsWithStderrInMessage()
    {
        string stderr = "crontab: error: PAM authentication failed";
        var ex = Assert.Throws<CrontabBackend.CrontabUnavailableException>(() =>
            CrontabBackend.InterpretReadResult(2, "", stderr));
        Assert.Contains("PAM authentication failed", ex.Message);
    }

    [Fact]
    public void NonZeroExit_EmptyStderr_ThrowsWithExitCodeInMessage()
    {
        // Belt-and-braces: any non-zero exit without a recognisable "no crontab for"
        // marker must surface, not silently return empty. Empty stderr is rare but
        // possible (some BSD variants); the diagnostic should still cite the exit code.
        var ex = Assert.Throws<CrontabBackend.CrontabUnavailableException>(() =>
            CrontabBackend.InterpretReadResult(1, "", ""));
        Assert.Contains("1", ex.Message);
    }

    [Fact]
    public void NonZeroExit_StderrIsTrimmedInMessage()
    {
        // Trailing newlines on stderr capture are common; the surfaced message should
        // be tidy single-line text suitable for an end-user error.
        var ex = Assert.Throws<CrontabBackend.CrontabUnavailableException>(() =>
            CrontabBackend.InterpretReadResult(1, "", "  spool locked  \n\n"));
        Assert.DoesNotContain("\n\n", ex.Message);
        Assert.Contains("spool locked", ex.Message);
    }

    [Fact]
    public void NonZeroExit_StdoutDiscardedWhenFailing()
    {
        // If crontab partially wrote stdout before failing, we must NOT return that
        // partial content as a baseline. The rest of the read-modify-write would then
        // overwrite the real crontab with the partial baseline.
        Assert.Throws<CrontabBackend.CrontabUnavailableException>(() =>
            CrontabBackend.InterpretReadResult(1, "0 2 * * * partial\n", "spool locked"));
    }
}
