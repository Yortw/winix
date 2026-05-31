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
    public void Known_exit_on_keys_parse(string expr)
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "inspect", "--exit-on", expr });
        Assert.True(r.Success);
        Assert.Equal(expr, r.Options!.ExitOn);
    }

    [Fact]
    public void Body_exit_on_parses_for_inspect()
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "inspect", "--exit-on", "body~ok" });
        Assert.True(r.Success);
        Assert.Equal("body~ok", r.Options!.ExitOn);
    }

    [Theory]   // A non-IP --host silently bound loopback before — now a usage error, not a silent intent-drop.
    [InlineData("localhost")]
    [InlineData("myhost.local")]
    [InlineData("192.168.1.5x")]
    [InlineData("not an ip")]
    public void Non_ip_host_is_usage_error(string host)
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "serve", "--host", host });
        Assert.False(r.Success);
        Assert.Contains("--host", r.Error!);
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("127.0.0.1")]
    [InlineData("192.168.1.5")]
    [InlineData("::1")]
    public void Ip_host_parses(string host)
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "serve", "--host", host });
        Assert.True(r.Success);
        Assert.Equal(host, r.Options!.Host);
    }

    [Theory]   // Serve and pipe never capture the body, so --exit-on body~ could never match — reject at parse.
    [InlineData("pipe")]
    [InlineData("serve")]
    public void Body_exit_on_is_usage_error_outside_inspect(string mode)
    {
        string[] argv = mode == "pipe"
            ? new[] { "pipe", "--exit-on", "body~done", "--", "cat" }
            : new[] { "serve", "--exit-on", "body~done" };
        ArgParser.Result r = ArgParser.Parse(argv);
        Assert.False(r.Success);
        Assert.Contains("body", r.Error!);
    }

    [Fact]
    public void Path_exit_on_parses_for_pipe()
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "pipe", "--exit-on", "path=/done", "--", "cat" });
        Assert.True(r.Success);
        Assert.Equal("path=/done", r.Options!.ExitOn);
    }

    [Fact]
    public void Path_exit_on_parses_for_serve()
    {
        ArgParser.Result r = ArgParser.Parse(new[] { "serve", "--exit-on", "path=/done" });
        Assert.True(r.Success);
        Assert.Equal("path=/done", r.Options!.ExitOn);
    }
}
