#nullable enable

using System.IO;
using System.Threading.Tasks;
using Winix.Winix;
using Xunit;

namespace Winix.Winix.Tests;

/// <summary>
/// CLI-level wiring tests for the <c>agents</c> scope flags. Both validation paths return before
/// any manifest fetch or file-system access, so a default (null) manifest loader is sufficient.
/// </summary>
public sealed class CliAgentsScopeTests
{
    [Fact]
    public async Task PathWithoutProject_IsUsageError()
    {
        var sw = new StringWriter();
        int code = await Cli.RunAsync(new[] { "agents", "status", "--path", "." }, sw, sw);
        Assert.Equal(WinixExitCode.UsageError, code);
        Assert.Contains("--path", sw.ToString(), System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProjectWithCodex_IsUsageError()
    {
        var sw = new StringWriter();
        int code = await Cli.RunAsync(new[] { "agents", "init", "--project", "--codex" }, sw, sw);
        Assert.Equal(WinixExitCode.UsageError, code);
        Assert.Contains("--codex", sw.ToString(), System.StringComparison.Ordinal);
    }
}
