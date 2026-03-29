using Xunit;
using Yort.ShellKit;

namespace Yort.ShellKit.Tests;

public class ConsoleEnvTests
{
    [Fact]
    public void ResolveUseColor_ExplicitColorFlag_ReturnsTrue()
    {
        bool result = ConsoleEnv.ResolveUseColor(
            colorFlag: true, noColorFlag: false, noColorEnv: false, isTerminal: false);

        Assert.True(result);
    }

    [Fact]
    public void ResolveUseColor_ExplicitNoColorFlag_ReturnsFalse()
    {
        bool result = ConsoleEnv.ResolveUseColor(
            colorFlag: false, noColorFlag: true, noColorEnv: false, isTerminal: true);

        Assert.False(result);
    }

    [Fact]
    public void ResolveUseColor_NoColorEnvSet_ReturnsFalse()
    {
        bool result = ConsoleEnv.ResolveUseColor(
            colorFlag: false, noColorFlag: false, noColorEnv: true, isTerminal: true);

        Assert.False(result);
    }

    [Fact]
    public void ResolveUseColor_ExplicitColorFlag_OverridesNoColorEnv()
    {
        bool result = ConsoleEnv.ResolveUseColor(
            colorFlag: true, noColorFlag: false, noColorEnv: true, isTerminal: false);

        Assert.True(result);
    }

    [Fact]
    public void ResolveUseColor_NoFlags_Terminal_ReturnsTrue()
    {
        bool result = ConsoleEnv.ResolveUseColor(
            colorFlag: false, noColorFlag: false, noColorEnv: false, isTerminal: true);

        Assert.True(result);
    }

    [Fact]
    public void ResolveUseColor_NoFlags_Piped_ReturnsFalse()
    {
        bool result = ConsoleEnv.ResolveUseColor(
            colorFlag: false, noColorFlag: false, noColorEnv: false, isTerminal: false);

        Assert.False(result);
    }
}
