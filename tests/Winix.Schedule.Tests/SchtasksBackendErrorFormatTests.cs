#nullable enable

using Winix.Schedule;
using Xunit;

namespace Winix.Schedule.Tests;

/// <summary>
/// R4 contract pins for the SchtasksBackend launch / timeout formatters extracted as part
/// of the I7 seam-extraction pass. The R3 polish commit (36dbf7b) added the elevation hint
/// for ERROR_ELEVATION_REQUIRED (740) — without these tests, a future refactor that swapped
/// the hint to "access denied" or dropped it entirely would lose the actionable cue and
/// the user would be left guessing.
/// </summary>
public sealed class SchtasksBackendErrorFormatTests
{
    [Fact]
    public void Format740_ElevationRequired_AppendsRunElevatedHint()
    {
        string msg = SchtasksBackend.FormatLaunchFailure(740, "The requested operation requires elevation.");

        Assert.Contains("Win32 error 740", msg);
        Assert.Contains("The requested operation requires elevation.", msg);
        Assert.Contains("(try running from an elevated command prompt)", msg);
    }

    [Fact]
    public void Format5_AccessDenied_AppendsAccessDeniedHint()
    {
        string msg = SchtasksBackend.FormatLaunchFailure(5, "Access is denied");

        Assert.Contains("Win32 error 5", msg);
        Assert.Contains("Access is denied", msg);
        Assert.Contains("(access denied — try running from an elevated command prompt)", msg);
    }

    [Theory]
    [InlineData(1, "Incorrect function")]
    [InlineData(8, "Not enough memory resources")]
    [InlineData(1450, "Insufficient system resources")]
    public void FormatGeneric_NoHintAppended(int errorCode, string message)
    {
        string msg = SchtasksBackend.FormatLaunchFailure(errorCode, message);

        Assert.Contains($"Win32 error {errorCode}", msg);
        Assert.Contains(message, msg);
        Assert.DoesNotContain("(try running from", msg);
        Assert.DoesNotContain("(access denied", msg);
    }

    [Fact]
    public void FormatLaunchFailure_PrefixIdentifiesBinary()
    {
        // All variants must lead with "could not launch schtasks.exe" so a user grepping
        // logs sees the source binary, not just an opaque Win32 code.
        Assert.StartsWith("could not launch schtasks.exe",
            SchtasksBackend.FormatLaunchFailure(740, "x"));
        Assert.StartsWith("could not launch schtasks.exe",
            SchtasksBackend.FormatLaunchFailure(5, "y"));
        Assert.StartsWith("could not launch schtasks.exe",
            SchtasksBackend.FormatLaunchFailure(0, "z"));
    }

    [Fact]
    public void FormatTimeoutFailure_RendersInSeconds()
    {
        Assert.Equal(
            "schtasks.exe did not respond within 30s",
            SchtasksBackend.FormatTimeoutFailure(30_000));
    }

    [Fact]
    public void FormatTimeoutFailure_SubSecondTimeoutFloors()
    {
        // Integer division floors; surfacing "0s" for a sub-second timeout is fine since
        // the timeout configuration is in milliseconds and this output is informational.
        Assert.Equal(
            "schtasks.exe did not respond within 0s",
            SchtasksBackend.FormatTimeoutFailure(500));
    }
}
