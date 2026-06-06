#nullable enable

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Yort.ShellKit;

namespace Winix.NetCat.Tests;

/// <summary>
/// Stage-1 wiring tests for <see cref="Cli.RunAsync"/> (seam ADR N6): the BuildOptions usage
/// matrix, check mode against real loopback sockets, and the N1 writer-wrap byte pin.
/// Newly-unlocked byte-path and cancellation tests live in CliRunAsyncUnlockedTests (stage 2).
/// </summary>
public class CliRunAsyncTests
{
    private static async Task<(int Exit, byte[] Stdout, string Stderr)> RunCliAsync(
        byte[]? stdinBytes, params string[] args)
    {
        using var stdin = new MemoryStream(stdinBytes ?? Array.Empty<byte>());
        using var stdout = new MemoryStream();
        var stderr = new StringWriter();
        int exit = await Cli.RunAsync(args, stdin, stdout, stderr, CancellationToken.None);
        return (exit, stdout.ToArray(), stderr.ToString());
    }

    /// <summary>Starts a real TCP listener on an OS-assigned loopback port; returns (listener, port).
    /// Caller stops the listener.</summary>
    private static (TcpListener Listener, int Port) StartLoopbackListener()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return (listener, port);
    }

    /// <summary>Returns a loopback port that is (momentarily) closed: bind, read, release.
    /// Inherent TOCTOU is acceptable — reuse within the test's microseconds is implausible.</summary>
    private static int GetClosedPort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    // --- BuildOptions usage matrix → 125 ---

    [Theory]
    [InlineData("--listen", "--check", "h", "80")]
    [InlineData("--tls", "--udp", "h", "80")]
    [InlineData("--tls", "--listen", "80")]
    [InlineData("--insecure", "h", "80")]
    [InlineData("--bind", "127.0.0.1", "h", "80")]
    [InlineData("--listen", "--bind", "not-an-ip", "80")]
    [InlineData("--listen", "--ipv4", "--bind", "::1", "80")]
    [InlineData("--listen", "--ipv6", "--bind", "127.0.0.1", "80")]
    [InlineData("--ipv4", "--ipv6", "h", "80")]
    [InlineData("--verbose", "h", "80")]
    [InlineData("--no-shutdown", "--check", "h", "80")]
    [InlineData("--listen", "80", "extra")]
    [InlineData("h")]
    [InlineData("-z", "h", "not-a-port")]
    [InlineData("h", "80-90")]
    public async Task UsageMatrix_Returns125_NothingOnStdout(params string[] args)
    {
        var r = await RunCliAsync(null, args);
        Assert.Equal(ExitCode.UsageError, r.Exit);
        Assert.NotEqual(string.Empty, r.Stderr);
        Assert.Empty(r.Stdout);
    }

    // --- Check mode vs real loopback sockets ---

    [Fact]
    public async Task Check_OpenPort_ExactBytesOnStdout_ExitZero()
    {
        var (listener, port) = StartLoopbackListener();
        try
        {
            var r = await RunCliAsync(null, "-z", "127.0.0.1", port.ToString());
            Assert.Equal(0, r.Exit);
            // The N1 writer-wrap byte pin: exact bytes incl. newline, identical to the old
            // Console.Out path under UseUtf8Streams (UTF-8, no BOM, Environment.NewLine).
            // VERIFIED 2026-06-06 against Formatting.FormatOpenPortLine (colour off → "{port} open").
            byte[] expected = Encoding.UTF8.GetBytes($"{port} open" + Environment.NewLine);
            Assert.Equal(expected, r.Stdout);
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public async Task Check_ClosedPort_Exit1_EmptyStdout()
    {
        int port = GetClosedPort();
        var r = await RunCliAsync(null, "-z", "127.0.0.1", port.ToString());
        Assert.Equal(1, r.Exit);
        Assert.Empty(r.Stdout);
    }

    [Fact]
    public async Task Check_ClosedPort_Verbose_LineOnStderr()
    {
        int port = GetClosedPort();
        var r = await RunCliAsync(null, "-z", "-v", "127.0.0.1", port.ToString());
        Assert.Equal(1, r.Exit);
        Assert.Contains(port.ToString(), r.Stderr, StringComparison.Ordinal);
    }

    /// <summary>Parses the LAST non-empty stderr line as JSON (adversarial-review F-3:
    /// only the cancel path's text-line-then-envelope ordering was probed; assuming
    /// whole-buffer purity for other paths is an unprobed assumption — last-line parsing
    /// is robust to both shapes and matches the stage-2 file's strategy).</summary>
    private static JsonDocument ParseLastLine(string stderr)
    {
        string[] lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return JsonDocument.Parse(lines[lines.Length - 1]);
    }

    [Fact]
    public async Task Check_Json_EnvelopeOnStderr_StdoutStaysEmpty()
    {
        // Round-3 CR-I6 pin: under --json the open-port TEXT lines are suppressed — stdout
        // must stay byte-empty; the envelope (with ports[]) goes to stderr.
        var (listener, port) = StartLoopbackListener();
        try
        {
            var r = await RunCliAsync(null, "-z", "--json", "127.0.0.1", port.ToString());
            Assert.Equal(0, r.Exit);
            Assert.Empty(r.Stdout);
            using var doc = ParseLastLine(r.Stderr);
            Assert.Equal("check", doc.RootElement.GetProperty("mode").GetString());
            Assert.Equal(0, doc.RootElement.GetProperty("exit_code").GetInt32());
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public async Task Check_DnsFailure_SummaryOnStderr_Exit1()
    {
        // Round-1 I-5 pin. The resolver text is OS-SPECIFIC ("Name or service not known" on
        // Linux, different on Windows) — assert only the stable prefix, never the OS text
        // (probed 2026-06-06).
        var r = await RunCliAsync(null, "-z", "winix-invalid-host-zz9.invalid", "80");
        Assert.Equal(1, r.Exit);
        Assert.Contains("port probes failed", r.Stderr, StringComparison.Ordinal);
        Assert.Empty(r.Stdout);
    }

    [Fact]
    public async Task Check_MixedOpenClosed_OpenLinePrinted_Exit1()
    {
        var (listener, openPort) = StartLoopbackListener();
        try
        {
            int closedPort = GetClosedPort();
            var r = await RunCliAsync(null, "-z", "127.0.0.1", $"{openPort},{closedPort}");
            Assert.Equal(1, r.Exit); // worst status wins
            Assert.Contains($"{openPort} open", Encoding.UTF8.GetString(r.Stdout), StringComparison.Ordinal);
        }
        finally { listener.Stop(); }
    }
}
