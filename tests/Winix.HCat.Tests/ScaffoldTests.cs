using Winix.HCat;
using Xunit;

namespace Winix.HCat.Tests;

public class ScaffoldTests
{
    [Fact]
    public void Cli_Run_is_reachable()
    {
        var so = new System.IO.StringWriter();
        var se = new System.IO.StringWriter();
        int code = Cli.Run(new[] { "--help" }, so, se);
        Assert.Equal(0, code);
    }
}
