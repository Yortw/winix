#nullable enable

using Winix.Schedule;
using Xunit;

namespace Winix.Schedule.Tests;

/// <summary>
/// R4 pins for <see cref="SchtasksBackend.IsBenignSchtasksEmpty"/> — the helper that
/// distinguishes "folder doesn't exist / no matching tasks" (fine) from real backend
/// failures (Task Scheduler service stopped, RPC unavailable, access denied, store
/// corruption). Pre-R4 every non-zero schtasks exit collapsed to an empty list, so the
/// user couldn't tell their service was wedged.
/// </summary>
public sealed class SchtasksBenignEmptyTests
{
    [Theory]
    [InlineData("ERROR: The system cannot find the file specified.")]
    [InlineData("error: the system cannot find the file specified")]
    [InlineData("INFO: There are no scheduled tasks in this folder.")]
    [InlineData("No tasks match")]
    [InlineData("INFO: No scheduled tasks present.")]
    public void RecognisedEmptyMarkers_ReturnTrue(string stderr)
    {
        Assert.True(SchtasksBackend.IsBenignSchtasksEmpty(stderr));
    }

    [Fact]
    public void EmptyStderr_TreatedAsBenignForBackwardsCompat()
    {
        // Some Windows Server SKUs return non-zero with empty stderr for "no tasks" —
        // pre-R4 behaviour was to swallow this. Keep that for compatibility, but ONLY
        // when stderr is genuinely silent. Any present text is treated as a real signal.
        Assert.True(SchtasksBackend.IsBenignSchtasksEmpty(""));
        Assert.True(SchtasksBackend.IsBenignSchtasksEmpty("   "));
        Assert.True(SchtasksBackend.IsBenignSchtasksEmpty(null!));
    }

    [Theory]
    [InlineData("ERROR: The Task Scheduler service is not available.")]
    [InlineData("ERROR: Access is denied.")]
    [InlineData("ERROR: The RPC server is unavailable.")]
    [InlineData("ERROR: The Task XML is malformed or contains values that are out of range.")]
    public void RealFailureSignatures_ReturnFalse(string stderr)
    {
        // Each of these is a genuine backend failure that pre-fix would have been mis-
        // reported as "no tasks." All must surface as Unavailable instead.
        Assert.False(SchtasksBackend.IsBenignSchtasksEmpty(stderr));
    }
}
