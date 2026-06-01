using Yort.ShellKit;
using Xunit;

namespace Yort.ShellKit.Tests;

public class ColorResolutionTests
{
    private static ParseResult Parse(params string[] args) =>
        new CommandLineParser("t", "1.0").StandardFlags().Parse(args);

    // Env-forced cases: result does not depend on TTY/NO_COLOR, so deterministic in any test host.

    [Fact]
    public void ColorAlways_ForcesOn()
    {
        Assert.True(Parse("--color=always").ResolveColor());
        Assert.True(Parse("--color").ResolveColor()); // bare → always
    }

    [Fact]
    public void ColorNever_ForcesOff()
    {
        Assert.False(Parse("--color=never").ResolveColor());
    }

    [Fact]
    public void NoColorFlag_ForcesOff()
    {
        Assert.False(Parse("--no-color").ResolveColor());
    }

    [Fact]
    public void ColorAlwaysAndNoColor_Tie_ColorWins()
    {
        // Preserves the existing "colorFlag wins" precedence in ConsoleEnv.ResolveUseColor.
        Assert.True(Parse("--color=always", "--no-color").ResolveColor());
    }

    // Full precedence table tested against the pure helper (no env/TTY dependence).

    [Theory]
    // colorFlag, noColorFlag, noColorEnv, isTerminal => expected
    [InlineData(true,  false, true,  false, true)]   // --color/always overrides NO_COLOR
    [InlineData(true,  true,  false, false, true)]   // colorFlag beats noColorFlag (tie → on)
    [InlineData(false, true,  false, true,  false)]  // never/--no-color off even on a TTY
    [InlineData(false, false, true,  true,  false)]  // NO_COLOR off
    [InlineData(false, false, false, true,  true)]   // auto + TTY → on
    [InlineData(false, false, false, false, false)]  // auto + non-TTY → off
    public void ResolveUseColor_Precedence(bool color, bool noColor, bool noColorEnv, bool isTty, bool expected)
    {
        Assert.Equal(expected, ConsoleEnv.ResolveUseColor(color, noColor, noColorEnv, isTty));
    }

    // T-C (F9): verifies the auto/absent → env/TTY mapping via the internal seam, host-independent.

    [Theory]
    [InlineData("--color=auto", true, false, true)]    // auto + TTY → on
    [InlineData("--color=auto", false, false, false)]  // auto + non-TTY → off
    [InlineData("--color=always", false, false, true)] // always forced on even non-TTY
    [InlineData("--color=never", true, false, false)]  // never forced off even on a TTY
    public void ResolveColorCore_MapsValueAndEnv(string arg, bool isTty, bool noColorEnv, bool expected)
    {
        var r = new CommandLineParser("t", "1.0").StandardFlags().Parse(new[] { arg });
        Assert.Equal(expected, r.ResolveColorCore(isTty, noColorEnv));
    }

    [Fact]
    public void ResolveColorCore_Absent_UsesTerminalBranch()
    {
        var r = new CommandLineParser("t", "1.0").StandardFlags().Parse(System.Array.Empty<string>());
        Assert.True(r.ResolveColorCore(isTerminal: true, noColorEnv: false));
        Assert.False(r.ResolveColorCore(isTerminal: false, noColorEnv: false));
    }
}
