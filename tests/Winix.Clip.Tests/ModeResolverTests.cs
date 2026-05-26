using Xunit;
using Winix.Clip;

namespace Winix.Clip.Tests;

public class ModeResolverTests
{
    [Fact]
    public void NoFlags_StdinHasNoContent_IsPaste()
    {
        var mode = ModeResolver.Resolve(stdinHasContent: false, new ClipOptions());
        Assert.Equal(ClipMode.Paste, mode);
    }

    [Fact]
    public void NoFlags_StdinHasContent_IsCopy()
    {
        var mode = ModeResolver.Resolve(stdinHasContent: true, new ClipOptions());
        Assert.Equal(ClipMode.Copy, mode);
    }

    [Fact]
    public void Clear_Wins_EvenWithStdinContent()
    {
        // --clear cannot coexist with -c/-p (ClipOptions rejects that), but stdin
        // state is ignored because ClipOptions was already validated.
        var mode = ModeResolver.Resolve(stdinHasContent: true, new ClipOptions(clear: true));
        Assert.Equal(ClipMode.Clear, mode);
    }

    [Fact]
    public void ForceCopy_StdinHasNoContent_IsStillCopy()
    {
        // Explicit -c overrides auto-detect — the user asked to copy, even if stdin is
        // empty (legitimate for "copy an empty value", e.g. clip -c < /dev/null).
        var mode = ModeResolver.Resolve(stdinHasContent: false, new ClipOptions(forceCopy: true));
        Assert.Equal(ClipMode.Copy, mode);
    }

    [Fact]
    public void ForcePaste_StdinHasContent_IsStillPaste()
    {
        var mode = ModeResolver.Resolve(stdinHasContent: true, new ClipOptions(forcePaste: true));
        Assert.Equal(ClipMode.Paste, mode);
    }

    // ── Tier-2 re-verification 2026-05-06 finding F2 ──
    // The auto-detection predicate moved from "stdin is redirected" to "stdin has
    // content" so Git Bash bare-clip no longer empties the clipboard. The pivotal
    // case is "redirected pipe with no bytes" — pre-fix this routed to copy mode
    // and silently overwrote the clipboard with empty; now it routes to paste.
    [Fact]
    public void NoFlags_StdinRedirectedButEmpty_IsPaste_GitBashQuirkClosed()
    {
        // Caller buffers stdin first; under Git Bash with no actual input piped,
        // the buffer is empty even though Console.IsInputRedirected is true.
        var mode = ModeResolver.Resolve(stdinHasContent: false, new ClipOptions());
        Assert.Equal(ClipMode.Paste, mode);
    }
}
