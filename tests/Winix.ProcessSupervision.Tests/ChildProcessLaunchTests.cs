using System.ComponentModel;
using Xunit;
using Yort.ShellKit;

namespace Winix.ProcessSupervision.Tests;

public class ChildProcessLaunchTests
{
    // ERROR_FILE_NOT_FOUND (2) / ERROR_PATH_NOT_FOUND (3) / ENOENT (2) → command not found.
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void ClassifyWin32_NotFoundCodes_ReturnsCommandNotFound(int nativeCode)
    {
        Exception result = ChildProcessLaunch.ClassifyWin32(new Win32Exception(nativeCode), "ghost");
        Assert.IsType<CommandNotFoundException>(result);
    }

    // ERROR_ACCESS_DENIED (5) / EACCES (13) → not executable.
    [Theory]
    [InlineData(5)]
    [InlineData(13)]
    public void ClassifyWin32_AccessCodes_ReturnsCommandNotExecutable(int nativeCode)
    {
        Exception result = ChildProcessLaunch.ClassifyWin32(new Win32Exception(nativeCode), "noperm");
        Assert.IsType<CommandNotExecutableException>(result);
    }

    // ERROR_BAD_EXE_FORMAT (193) and any other code → not-executable, message preserved,
    // original Win32Exception retained as inner for diagnostics (NOT the single-arg ctor,
    // which prepends a misleading "permission denied:"). Also pins the "message verbatim"
    // claim: Win32Exception.Message comes from the OS (FormatMessage), not .NET resources,
    // so it is unaffected by UseSystemResourceKeys — assert it carries the command name and
    // is non-empty rather than assuming so.
    [Fact]
    public void ClassifyWin32_OtherCode_ReturnsNotExecutableWithInner()
    {
        var win32 = new Win32Exception(193);
        Exception result = ChildProcessLaunch.ClassifyWin32(win32, "badexe");
        Assert.IsType<CommandNotExecutableException>(result);
        Assert.Same(win32, result.InnerException);
        Assert.Contains("badexe", result.Message, System.StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }
}
