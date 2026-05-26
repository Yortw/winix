using Xunit;
using Winix.Clip;

namespace Winix.Clip.Tests;

public class ClipOptionsTests
{
    [Fact]
    public void Construct_WithDefaults_SetsExpectedValues()
    {
        var options = new ClipOptions();

        Assert.False(options.ForceCopy);
        Assert.False(options.ForcePaste);
        Assert.False(options.Clear);
        Assert.False(options.Raw);
        Assert.False(options.Primary);
    }

    [Fact]
    public void Construct_WithForceCopyAndForcePaste_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new ClipOptions(forceCopy: true, forcePaste: true));
    }

    [Fact]
    public void Construct_WithClearAndForceCopy_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new ClipOptions(clear: true, forceCopy: true));
    }

    [Fact]
    public void Construct_WithClearAndForcePaste_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new ClipOptions(clear: true, forcePaste: true));
    }

    [Fact]
    public void Construct_WithClearAndRaw_Throws()
    {
        // --raw is only meaningful on paste; --clear excludes paste.
        Assert.Throws<ArgumentException>(() =>
            new ClipOptions(clear: true, raw: true));
    }
}
