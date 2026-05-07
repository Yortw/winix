#nullable enable

using System.Collections.Generic;
using Xunit;
using Winix.Clip;

namespace Winix.Clip.Tests;

/// <summary>
/// Round-2 SFH I1 regression tests. The DefaultProcessRunner wraps two failure modes
/// of the underlying Process API as <see cref="ClipboardException"/> so the dispatch
/// catch in <see cref="Cli.Run"/> surfaces them with the documented exit code:
/// <list type="bullet">
///   <item>Win32Exception from <c>Process.Start</c> when the binary cannot be located
///   or executed (e.g. missing pbcopy on a corrupted macOS install).</item>
///   <item>IOException from the stdin write when the helper crashed before consuming
///   all of stdin (broken pipe).</item>
/// </list>
/// Pre-fix both leaked as raw .NET exceptions, producing a stack trace on stderr
/// instead of <c>clip: ...</c> + exit 126.
/// </summary>
public class DefaultProcessRunnerTests
{
    [Fact]
    public void Run_NonExistentBinary_ThrowsClipboardException()
    {
        // Spawn a binary name that cannot exist on either Windows or POSIX systems.
        // Process.Start raises Win32Exception (ERROR_FILE_NOT_FOUND on Windows,
        // ENOENT on POSIX). The runner must wrap it.
        var runner = new DefaultProcessRunner();

        ClipboardException ex = Assert.Throws<ClipboardException>(() =>
            runner.Run("winix-definitely-not-a-real-binary-9999", new List<string>(), stdin: null));

        Assert.Contains("failed to launch", ex.Message, StringComparison.Ordinal);
        Assert.Contains("winix-definitely-not-a-real-binary-9999", ex.Message, StringComparison.Ordinal);
        // Inner exception preserved so diagnostics aren't lost.
        Assert.NotNull(ex.InnerException);
    }
}
