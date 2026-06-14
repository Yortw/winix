using System.Threading;
using Xunit;
using Yort.ShellKit;

namespace Winix.ProcessSupervision.Tests;

public class ChildProcessRunnerTests
{
    [Fact]
    public void Run_ChildExitsZero_ReturnsZero()
    {
        var runner = new ChildProcessRunner();
        (string cmd, string[] args) = ChildHelpers.ExitWith(0);

        int code = runner.Run(cmd, args, CancellationToken.None);

        Assert.Equal(0, code);
    }

    [Fact]
    public void Run_ChildExitsNonZero_ForwardsExitCode()
    {
        var runner = new ChildProcessRunner();
        (string cmd, string[] args) = ChildHelpers.ExitWith(7);

        int code = runner.Run(cmd, args, CancellationToken.None);

        Assert.Equal(7, code);
    }

    [Fact]
    public void Run_CommandNotFound_ThrowsCommandNotFound()
    {
        var runner = new ChildProcessRunner();

        Assert.Throws<CommandNotFoundException>(() =>
            runner.Run("this-command-does-not-exist-xyzzy", System.Array.Empty<string>(), CancellationToken.None));
    }
}
