using Winix.HCat;
using Xunit;

namespace Winix.HCat.Tests;

public class ArgParserTests
{
    [Fact]
    public void Bare_invocation_is_serve_cwd_loopback()
    {
        ArgParser.Result r = ArgParser.Parse(new string[0]);
        Assert.True(r.Success);
        Assert.Equal(HCatMode.Serve, r.Options!.Mode);
        Assert.Equal(".", r.Options.Directory);
        Assert.False(r.Options.Lan);
    }

    [Fact]
    public void Serve_with_dir_and_lan()
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "serve", "./public", "--lan", "--port", "9000" });
        Assert.True(r.Success);
        Assert.Equal(HCatMode.Serve, r.Options!.Mode);
        Assert.Equal("./public", r.Options.Directory);
        Assert.True(r.Options.Lan);
        Assert.Equal(9000, r.Options.Port);
    }

    [Fact]
    public void Inspect_with_status()
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "inspect", "--status", "204" });
        Assert.True(r.Success);
        Assert.Equal(HCatMode.Inspect, r.Options!.Mode);
        Assert.Equal(204, r.Options.InspectStatus);
    }

    [Fact]
    public void Pipe_captures_command_after_double_dash()
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "pipe", "--", "jq", "." });
        Assert.True(r.Success);
        Assert.Equal(HCatMode.Pipe, r.Options!.Mode);
        Assert.Equal(new[] { "jq", "." }, r.Options.PipeCommand);
    }

    [Fact]
    public void Unknown_subcommand_is_usage_error_with_hint()
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "frobnicate" });
        Assert.False(r.Success);
        Assert.False(r.IsHandled);
        Assert.Contains("did you mean", r.Error!);
    }

    [Fact]
    public void Upload_flags_parse()
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "serve", "--upload", "--upload-dir", "./incoming" });
        Assert.True(r.Success);
        Assert.True(r.Options!.Upload);
        Assert.Equal("./incoming", r.Options.UploadDir);
    }

    [Fact]
    public void Capture_and_timeout_parse()
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "inspect", "--capture", "3", "--timeout", "30s" });
        Assert.True(r.Success);
        Assert.Equal(3, r.Options!.CaptureCount);
        Assert.Equal(System.TimeSpan.FromSeconds(30), r.Options.Timeout);
    }

    [Theory]   // F13: an unknown --exit-on key is a usage error, not a silent never-match footgun.
    [InlineData("paht=/done")]
    [InlineData("status=200")]
    [InlineData("garbage")]
    public void Unknown_exit_on_key_is_usage_error(string expr)
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "inspect", "--exit-on", expr });
        Assert.False(r.Success);
        Assert.Contains("--exit-on", r.Error!);
    }

    [Theory]
    [InlineData("path=/done")]
    [InlineData("method=POST")]
    [InlineData("body~ok")]
    public void Known_exit_on_keys_parse(string expr)
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "inspect", "--exit-on", expr });
        Assert.True(r.Success);
        Assert.Equal(expr, r.Options!.ExitOn);
    }
}
