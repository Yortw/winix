using System.IO;
using Winix.HCat;
using Xunit;

namespace Winix.HCat.Tests;

/// <summary>Unit tests for the <see cref="Cli.Run"/> seam: help/version handling and usage-error mapping.
/// These exercise the parse → exit-code wiring without starting the server (no positional/flag combination
/// here reaches <see cref="HCatServer.RunAsync"/>).</summary>
public class CliTests
{
    [Fact]
    public void Help_returns_0()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = Cli.Run(new[] { "--help" }, stdout, stderr);
        Assert.Equal(0, code);
    }

    [Fact]
    public void Unknown_subcommand_returns_125()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = Cli.Run(new[] { "frobnicate" }, stdout, stderr);
        Assert.Equal(125, code);
        // Human-readable usage error on stderr, not an SR key.
        Assert.Contains("hcat:", stderr.ToString());
        Assert.Contains("Run 'hcat --help' for usage.", stderr.ToString());
    }
}
