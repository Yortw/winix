using System;
using System.Diagnostics;
using Xunit;
using Yort.ShellKit;

namespace Winix.ProcessSupervision.Tests;

public class ChildProcessLauncherTests
{
    [Fact]
    public void Launch_ValidCommand_ReturnsRunningProcess_ThatExitsWithItsCode()
    {
        (string cmd, string[] args) = ChildHelpers.ExitWith(5);

        using Process p = ChildProcessLauncher.Launch(cmd, args);
        p.WaitForExit();

        Assert.Equal(5, p.ExitCode);
    }

    [Fact]
    public void Launch_CommandNotFound_ThrowsCommandNotFound()
    {
        Assert.Throws<CommandNotFoundException>(() =>
            ChildProcessLauncher.Launch("this-command-does-not-exist-xyzzy", Array.Empty<string>()));
    }
}
