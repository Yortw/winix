using Xunit;

namespace Winix.ProcessSupervision.Tests;

public class UnixSignalTests
{
    [Theory]
    [InlineData("TERM", 15)]
    [InlineData("term", 15)]
    [InlineData("SIGTERM", 15)]
    [InlineData("HUP", 1)]
    [InlineData("INT", 2)]
    [InlineData("QUIT", 3)]
    [InlineData("KILL", 9)]
    public void TryParse_KnownNames_ReturnsNumber(string name, int expected)
    {
        Assert.True(UnixSignal.TryParse(name, out int signal));
        Assert.Equal(expected, signal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("BOGUS")]
    [InlineData("SIGBOGUS")]
    [InlineData("9")] // numeric form not supported in v1 — names only
    public void TryParse_UnknownOrEmpty_ReturnsFalse(string name)
    {
        Assert.False(UnixSignal.TryParse(name, out int signal));
        Assert.Equal(0, signal);
    }

    [Fact]
    public void ToName_KnownNumber_RoundTrips()
    {
        Assert.Equal("TERM", UnixSignal.ToName(15));
        Assert.Equal("KILL", UnixSignal.ToName(9));
        Assert.Equal("HUP", UnixSignal.ToName(1));
        Assert.Equal("INT", UnixSignal.ToName(2));
        Assert.Equal("QUIT", UnixSignal.ToName(3));
    }

    [Fact]
    public void ToName_UnknownNumber_FallsBackToDecimalString()
    {
        // The default arm formats the raw number (invariant culture) — pins it so a future change that
        // returns "" or "unknown" for an out-of-set signal fails loudly.
        Assert.Equal("99", UnixSignal.ToName(99));
    }
}
