using System;
using Xunit;

namespace Winix.RunFor.Tests;

public class FormattingTests
{
    [Fact]
    public void FormatJson_TimedOut_HasEnvelopeAndTimeoutFields()
    {
        RunForResult r = RunForResult.TimedOut(TimeSpan.FromMilliseconds(5003), killFailed: false);
        string json = Formatting.FormatJson(r, "runfor", "1.2.3", signalName: "TERM");

        Assert.Contains("\"tool\":\"runfor\"", json);
        Assert.Contains("\"version\":\"1.2.3\"", json);
        Assert.Contains("\"exit_code\":124", json);
        Assert.Contains("\"outcome\":\"timed_out\"", json);
        Assert.Contains("\"timed_out\":true", json);
        Assert.Contains("\"child_exit_code\":null", json);
        Assert.Contains("\"signal\":\"TERM\"", json);
        Assert.Contains("\"kill_failed\":false", json);
        Assert.Contains("\"duration_ms\":5003", json);
    }

    [Fact]
    public void FormatJson_Completed_CarriesChildCode_TimedOutFalse()
    {
        RunForResult r = RunForResult.Completed(0, TimeSpan.FromSeconds(1));
        string json = Formatting.FormatJson(r, "runfor", "1.2.3", signalName: "TERM");

        Assert.Contains("\"outcome\":\"completed\"", json);
        Assert.Contains("\"timed_out\":false", json);
        Assert.Contains("\"child_exit_code\":0", json);
    }

    [Fact]
    public void FormatJson_Interrupted_OutcomeInterrupted_TimedOutFalse_NullChild()
    {
        // Pins the Interrupted JSON path: it is NOT a timeout (timed_out:false) and has no child code.
        RunForResult r = RunForResult.Interrupted(TimeSpan.FromSeconds(2), killFailed: false);
        string json = Formatting.FormatJson(r, "runfor", "1.2.3", signalName: "TERM");

        Assert.Contains("\"outcome\":\"interrupted\"", json);
        Assert.Contains("\"timed_out\":false", json);
        Assert.Contains("\"child_exit_code\":null", json);
    }

    [Fact]
    public void FormatJson_LaunchFailed_OutcomeLaunchFailed_TimedOutFalse_NullChild()
    {
        RunForResult r = RunForResult.LaunchFailed(127, TimeSpan.Zero);
        string json = Formatting.FormatJson(r, "runfor", "1.2.3", signalName: "TERM");

        Assert.Contains("\"outcome\":\"launch_failed\"", json);
        Assert.Contains("\"timed_out\":false", json);
        Assert.Contains("\"child_exit_code\":null", json);
    }

    [Fact]
    public void FormatNotice_TimedOut_MentionsDeadline()
    {
        string notice = Formatting.FormatNotice(
            RunForResult.TimedOut(TimeSpan.FromSeconds(5), killFailed: false),
            command: "sleep", deadline: TimeSpan.FromSeconds(5), useColor: false);

        Assert.Contains("runfor", notice);
        Assert.Contains("timed out", notice, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sleep", notice);
    }

    [Fact]
    public void FormatNotice_Completed_IsEmpty()
    {
        string notice = Formatting.FormatNotice(
            RunForResult.Completed(0, TimeSpan.FromSeconds(1)),
            command: "echo", deadline: TimeSpan.FromSeconds(5), useColor: false);
        Assert.Equal(string.Empty, notice);
    }

    [Fact]
    public void FormatNotice_LaunchFailed_IsEmpty()
    {
        // The launch-failure message is the caller's job (it prints the classified reason); this notice
        // must stay silent so the user never sees a contradictory "timed out"/"interrupted" line.
        string notice = Formatting.FormatNotice(
            RunForResult.LaunchFailed(127, TimeSpan.Zero),
            command: "nope", deadline: TimeSpan.FromSeconds(5), useColor: false);
        Assert.Equal(string.Empty, notice);
    }

    [Fact]
    public void FormatNotice_Interrupted_MentionsInterrupted()
    {
        string notice = Formatting.FormatNotice(
            RunForResult.Interrupted(TimeSpan.Zero, killFailed: false),
            command: "sleep", deadline: TimeSpan.FromSeconds(5), useColor: false);

        Assert.Contains("runfor", notice);
        Assert.Contains("interrupted", notice, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sleep", notice);
    }

    [Fact]
    public void FormatNotice_KillFailed_IncludesWarning()
    {
        // Diagnosability: when the kill could not be confirmed the user MUST be warned the child may
        // still be running. Pins the warning text on the kill-failed path (zero coverage otherwise).
        string notice = Formatting.FormatNotice(
            RunForResult.TimedOut(TimeSpan.FromSeconds(5), killFailed: true),
            command: "sleep", deadline: TimeSpan.FromSeconds(5), useColor: false);

        Assert.Contains("could not terminate", notice, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("may still be running", notice, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FormatNotice_ColorFalse_NoAnsi()
    {
        // Suite "--color wired?" class: with useColor false the notice must carry NO ANSI escape.
        // Byte-precise per feedback_ansi_test_char27 — raw-ESC literals round-trip unreliably.
        string notice = Formatting.FormatNotice(
            RunForResult.TimedOut(TimeSpan.FromSeconds(5), killFailed: true),
            command: "sleep", deadline: TimeSpan.FromSeconds(5), useColor: false);

        Assert.DoesNotContain(((char)27).ToString(), notice, StringComparison.Ordinal);
    }
}
