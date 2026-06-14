using Xunit;

namespace Winix.ProcessSupervision.Tests;

public class SupervisionExitCodeTests
{
    // coreutils `timeout` exits 124 on deadline; runfor matches it.
    [Fact]
    public void Timeout_Is124()
    {
        Assert.Equal(124, SupervisionExitCode.Timeout);
    }

    // 128 + SIGINT(2); the shell convention for Ctrl+C.
    [Fact]
    public void Interrupted_Is130()
    {
        Assert.Equal(130, SupervisionExitCode.Interrupted);
    }
}
