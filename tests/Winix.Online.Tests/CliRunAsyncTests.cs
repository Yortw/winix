#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Winix.Online;
using Xunit;

namespace Winix.Online.Tests;

public class CliRunAsyncTests
{
    // Seams that make the internet check pass instantly with no real network or waiting.
    private static OnlineSeams HealthySeams() => new(
        RouteAvailable: () => true,
        DnsProbe: (_, _) => Task.FromResult(true),
        HttpProbe: (_, _) => Task.FromResult(new HttpProbeResult(true, 204)),
        EndpointOrder: e => e,
        Now: () => DateTimeOffset.UnixEpoch,
        Sleep: (_, _) => Task.CompletedTask);

    [Fact]
    public async Task Once_internet_healthy_returns_0()
    {
        var outW = new StringWriter();
        var errW = new StringWriter();
        int code = await Cli.RunAsync(new[] { "--once" }, outW, errW, CancellationToken.None, HealthySeams());
        Assert.Equal(0, code);
    }

    [Fact]
    public async Task Once_internet_down_returns_1()
    {
        var seams = new OnlineSeams(
            RouteAvailable: () => false,
            DnsProbe: (_, _) => Task.FromResult(true),
            HttpProbe: (_, _) => Task.FromResult(new HttpProbeResult(true, 204)),
            EndpointOrder: e => e,
            Now: () => DateTimeOffset.UnixEpoch,
            Sleep: (_, _) => Task.CompletedTask);
        int code = await Cli.RunAsync(new[] { "--once" }, TextWriter.Null, TextWriter.Null, CancellationToken.None, seams);
        Assert.Equal(1, code);
    }

    [Fact]
    public async Task Timeout_returns_124()
    {
        // Clock that jumps past the 1s budget on the second read so the loop times out fast.
        var times = new Queue<DateTimeOffset>(new[]
        {
            DateTimeOffset.UnixEpoch,                          // start
            DateTimeOffset.UnixEpoch,                          // after cycle 1 (elapsed in result)
            DateTimeOffset.UnixEpoch + TimeSpan.FromSeconds(5),// deadline check — past 1s budget
            DateTimeOffset.UnixEpoch + TimeSpan.FromSeconds(5),// elapsed in result
        });
        var seams = new OnlineSeams(
            RouteAvailable: () => false,                       // never ready
            DnsProbe: (_, _) => Task.FromResult(false),
            HttpProbe: (_, _) => Task.FromResult(HttpProbeResult.Unreachable),
            EndpointOrder: e => e,
            Now: () => times.Count > 1 ? times.Dequeue() : times.Peek(),
            Sleep: (_, _) => Task.CompletedTask);
        int code = await Cli.RunAsync(new[] { "--timeout", "1s" }, TextWriter.Null, TextWriter.Null, CancellationToken.None, seams);
        Assert.Equal(124, code);
    }

    [Fact]
    public async Task Json_envelope_goes_to_stdout()
    {
        var outW = new StringWriter();
        var errW = new StringWriter();
        int code = await Cli.RunAsync(new[] { "--once", "--json" }, outW, errW, CancellationToken.None, HealthySeams());
        Assert.Equal(0, code);
        Assert.Contains("\"ready\":true", outW.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("\"ready\"", errW.ToString(), StringComparison.Ordinal);  // not on stderr
    }

    [Theory]
    [InlineData("--timeout", "notaduration")]
    [InlineData("--status", "abc")]
    [InlineData("--url", "not-a-url")]
    [InlineData("--interval", "0")]
    [InlineData("--probe-timeout", "0")]
    public async Task Invalid_args_return_125(string flag, string value)
    {
        int code = await Cli.RunAsync(new[] { flag, value }, TextWriter.Null, TextWriter.Null, CancellationToken.None, HealthySeams());
        Assert.Equal(125, code);
    }

    // F4 — silent no-op flags become usage errors, not silently-ignored input.
    [Fact]
    public async Task Endpoint_without_internet_is_usage_error()
    {
        // --url present, --internet absent ⇒ internet check off ⇒ --endpoint override would be discarded.
        var errW = new StringWriter();
        int code = await Cli.RunAsync(
            new[] { "--url", "https://x.example/health", "--endpoint", "https://my.example/generate_204", "--once" },
            TextWriter.Null, errW, CancellationToken.None, HealthySeams());
        Assert.Equal(125, code);
        Assert.Contains("--endpoint", errW.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_without_url_is_usage_error()
    {
        var errW = new StringWriter();
        int code = await Cli.RunAsync(
            new[] { "--status", "200", "--once" },   // bare online (internet) — --status has no target
            TextWriter.Null, errW, CancellationToken.None, HealthySeams());
        Assert.Equal(125, code);
        Assert.Contains("--status", errW.ToString(), StringComparison.Ordinal);
    }

    // F5 — Ctrl+C mid-wait surfaces as exit 130 + "interrupted", not a misleading exit 1.
    [Fact]
    public async Task Cancelled_token_returns_130_interrupted()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var errW = new StringWriter();
        int code = await Cli.RunAsync(new[] { "--internet" }, TextWriter.Null, errW, cts.Token, HealthySeams());
        Assert.Equal(130, code);
        Assert.Contains("interrupted", errW.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Seam_that_throws_is_mapped_not_crashed()
    {
        var seams = new OnlineSeams(
            RouteAvailable: () => true,
            DnsProbe: (_, _) => Task.FromResult(true),
            HttpProbe: (_, _) => throw new InvalidOperationException("boom"),
            EndpointOrder: e => e,
            Now: () => DateTimeOffset.UnixEpoch,
            Sleep: (_, _) => Task.CompletedTask);
        var errW = new StringWriter();
        int code = await Cli.RunAsync(new[] { "--once" }, TextWriter.Null, errW, CancellationToken.None, seams);
        Assert.Equal(ExitCodeForUnexpected, code);
        Assert.Contains("online:", errW.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException\n   at ", errW.ToString(), StringComparison.Ordinal); // no stack trace
    }

    [Fact]
    public async Task Timeout_zero_is_accepted_not_usage_error()
    {
        // --timeout 0 = wait forever; with --once + healthy it completes one cycle → exit 0 (not 125).
        int code = await Cli.RunAsync(
            new[] { "--once", "--timeout", "0" },
            TextWriter.Null, TextWriter.Null, CancellationToken.None, HealthySeams());
        Assert.Equal(0, code);
    }

    // Unexpected-error exit code chosen in Cli (see implementation): 126 (NotExecutable-style "tool fault").
    private const int ExitCodeForUnexpected = 126;
}
