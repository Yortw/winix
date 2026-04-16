using Xunit;
using Winix.Clip;

namespace Winix.Clip.Tests;

public class ModeResolverTests
{
    [Fact]
    public void NoFlags_StdinNotRedirected_IsPaste()
    {
        var mode = ModeResolver.Resolve(stdinRedirected: false, new ClipOptions());
        Assert.Equal(ClipMode.Paste, mode);
    }

    [Fact]
    public void NoFlags_StdinRedirected_IsCopy()
    {
        var mode = ModeResolver.Resolve(stdinRedirected: true, new ClipOptions());
        Assert.Equal(ClipMode.Copy, mode);
    }

    [Fact]
    public void Clear_Wins_EvenWithStdinRedirected()
    {
        // --clear cannot coexist with -c/-p (ClipOptions rejects that), but stdin
        // state is ignored because ClipOptions was already validated.
        var mode = ModeResolver.Resolve(stdinRedirected: true, new ClipOptions(clear: true));
        Assert.Equal(ClipMode.Clear, mode);
    }

    [Fact]
    public void ForceCopy_StdinNotRedirected_IsCopy()
    {
        var mode = ModeResolver.Resolve(stdinRedirected: false, new ClipOptions(forceCopy: true));
        Assert.Equal(ClipMode.Copy, mode);
    }

    [Fact]
    public void ForcePaste_StdinRedirected_IsPaste()
    {
        var mode = ModeResolver.Resolve(stdinRedirected: true, new ClipOptions(forcePaste: true));
        Assert.Equal(ClipMode.Paste, mode);
    }
}
