using System;
using Winix.ProcessSupervision;
using Xunit;

namespace Winix.RunFor.Tests;

public class RunForOptionsTests
{
    [Fact]
    public void Constructs_WithDefaults()
    {
        var o = new RunForOptions(TimeSpan.FromSeconds(5), UnixSignal.DefaultSignal, killAfter: null);
        Assert.Equal(TimeSpan.FromSeconds(5), o.Deadline);
        Assert.Equal(15, o.Signal);
        Assert.Null(o.KillAfter);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void ZeroOrNegativeDeadline_Throws(int seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RunForOptions(TimeSpan.FromSeconds(seconds), UnixSignal.DefaultSignal, null));
    }

    [Fact]
    public void NegativeKillAfter_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RunForOptions(TimeSpan.FromSeconds(5), UnixSignal.DefaultSignal, TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void KillAfterZero_IsAccepted()
    {
        // Boundary: zero grace is VALID — it means "escalate to SIGKILL immediately after the signal".
        // Only NEGATIVE is rejected. Pins the guard at `< Zero` so a future tightening to `<= Zero`
        // (which would wrongly reject zero) fails loudly here.
        var o = new RunForOptions(TimeSpan.FromSeconds(5), UnixSignal.DefaultSignal, TimeSpan.Zero);
        Assert.Equal(TimeSpan.Zero, o.KillAfter);
    }
}
