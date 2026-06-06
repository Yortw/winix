#nullable enable

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Winix.NetCat.Tests;

/// <summary>
/// Stage-2: the previously-impossible coverage the byte-stream seam unlocks (seam ADR N6) —
/// the full in-process byte path (MemoryStream ↔ real loopback socket) for connect and listen,
/// and deterministic cancellation envelopes. Added after stage-1 neutrality validation.
/// </summary>
public class CliRunAsyncUnlockedTests
{
    /// <summary>Parses the LAST non-empty stderr line as JSON. Probed 2026-06-06: nc's cancel
    /// path writes the plain "nc: interrupted" line BEFORE the envelope (unlike wargs's
    /// envelope-only discipline) — whole-buffer parsing would throw.</summary>
    private static JsonDocument ParseLastLine(string stderr)
    {
        string[] lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return JsonDocument.Parse(lines[lines.Length - 1]);
    }

    // --- Connect-mode byte path ---

    [Fact]
    public async Task Connect_BytePath_NonUtf8BytesRoundTrip()
    {
        // In-process echo server: accept one connection, read to EOF (peer half-close),
        // echo everything back, close. The payload includes bytes invalid as UTF-8 —
        // proving the seam carries BYTES, not text.
        byte[] payload = { 0x00, 0x01, 0xFF, 0xFE, 0x80, 0x47, 0x45, 0x54, 0x0A, 0xC0 };
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Task serverTask = Task.Run(async () =>
        {
            using TcpClient peer = await listener.AcceptTcpClientAsync();
            using NetworkStream ns = peer.GetStream();
            using var buffer = new MemoryStream();
            await ns.CopyToAsync(buffer); // reads until nc half-closes after stdin EOF
            buffer.Position = 0;
            await buffer.CopyToAsync(ns);
            // close → nc sees remote EOF → exits
        });

        try
        {
            using var stdin = new MemoryStream(payload);
            using var stdout = new MemoryStream();
            var stderr = new StringWriter();
            int exit = await Cli.RunAsync(
                new[] { "--json", "127.0.0.1", port.ToString() }, stdin, stdout, stderr, CancellationToken.None);
            await serverTask;

            Assert.Equal(0, exit);
            Assert.Equal(payload, stdout.ToArray());
            using var doc = ParseLastLine(stderr.ToString());
            Assert.Equal("connect", doc.RootElement.GetProperty("mode").GetString());
            Assert.Equal(payload.Length, doc.RootElement.GetProperty("bytes_sent").GetInt32());
            Assert.Equal(payload.Length, doc.RootElement.GetProperty("bytes_received").GetInt32());
        }
        finally { listener.Stop(); }
    }

    // --- Listen-mode byte path ---

    /// <summary>Detects the listen-bind TOCTOU race (adversarial-review F-1): nc losing the
    /// freed port to another process surfaces as the catch-all's 126 + "unexpected error".
    /// Tests retry with a fresh port instead of failing — pinned UP FRONT, not as a
    /// post-flake patch (the client-connect retry alone cannot cure a bind failure).</summary>
    private static bool IsPortBindRace(int exit, string stderr)
        => exit == 126 && stderr.Contains("unexpected error", StringComparison.Ordinal);

    [Fact]
    public async Task Listen_BytePath_ReceivesClientBytesOnStdout()
    {
        byte[] payload = { 0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x0A };
        for (int attempt = 0; ; attempt++)
        {
            int port = GetProbablyFreePort();
            using var stdin = new MemoryStream(); // nothing to send
            using var stdout = new MemoryStream();
            var stderr = new StringWriter();
            Task<int> ncTask = Cli.RunAsync(
                new[] { "--json", "-l", port.ToString() }, stdin, stdout, stderr, CancellationToken.None);

            // Connect after a short readiness wait (poll-connect handles the startup race
            // without a fixed sleep). If nc lost the bind race it exits 126 immediately and
            // the client connect can never succeed — detect that FIRST (the connect retry
            // exhausting would otherwise throw before the race check is reached).
            try
            {
                using var client = new TcpClient();
                await ConnectWithRetryAsync(client, port, attempts: 50, delayMs: 100);
                using NetworkStream ns = client.GetStream();
                await ns.WriteAsync(payload);
            } // dispose closes → nc sees EOF → exits
            catch (SocketException) when (ncTask.IsCompleted && attempt < 2)
            {
                // ncTask.IsCompleted makes this await non-blocking — no .Result (the
                // no-blocking-on-async rule is absolute; awaiting a completed task is the
                // rule-compliant way to read its value in a catch).
                int earlyExit = await ncTask;
                if (IsPortBindRace(earlyExit, stderr.ToString()))
                {
                    continue; // nc lost the freed port to another process — retry with a fresh one
                }
                throw;
            }

            int exit = await ncTask;
            if (IsPortBindRace(exit, stderr.ToString()) && attempt < 2) { continue; }

            Assert.Equal(0, exit);
            Assert.Equal(payload, stdout.ToArray());
            using var doc = ParseLastLine(stderr.ToString());
            Assert.Equal("listen", doc.RootElement.GetProperty("mode").GetString());
            return;
        }
    }

    private static int GetProbablyFreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static async Task ConnectWithRetryAsync(TcpClient client, int port, int attempts, int delayMs)
    {
        for (int i = 0; ; i++)
        {
            try { await client.ConnectAsync(IPAddress.Loopback, port); return; }
            catch (SocketException) when (i < attempts - 1) { await Task.Delay(delayMs); }
        }
    }

    // --- Cancellation (probed contracts — failures are real signals, do not soften) ---

    [Fact]
    public async Task Listen_PreCancelledToken_130_InterruptedEnvelope()
    {
        using var stdin = new MemoryStream();
        using var stdout = new MemoryStream();
        var stderr = new StringWriter();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        int exit = await Cli.RunAsync(
            new[] { "--json", "-l", GetProbablyFreePort().ToString() }, stdin, stdout, stderr, cts.Token);
        Assert.Equal(130, exit);
        Assert.Contains("nc: interrupted", stderr.ToString(), StringComparison.Ordinal);
        using var doc = ParseLastLine(stderr.ToString());
        Assert.Equal("interrupted", doc.RootElement.GetProperty("exit_reason").GetString());
        Assert.Equal(130, doc.RootElement.GetProperty("exit_code").GetInt32());
    }

    [Fact]
    public async Task Listen_MidAcceptCancel_130_Promptly()
    {
        // The deterministic hanging case (probed): listen-accept waits forever; cancel at
        // 300ms must abort it. Liveness bound is coarse (not perf). Same bind-race retry
        // as Listen_BytePath (adversarial-review F-1).
        for (int attempt = 0; ; attempt++)
        {
            using var stdin = new MemoryStream();
            using var stdout = new MemoryStream();
            var stderr = new StringWriter();
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(300);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int exit = await Cli.RunAsync(
                new[] { "-l", GetProbablyFreePort().ToString() }, stdin, stdout, stderr, cts.Token);
            sw.Stop();
            if (IsPortBindRace(exit, stderr.ToString()) && attempt < 2) { continue; }

            Assert.Equal(130, exit);
            Assert.Contains("nc: interrupted", stderr.ToString(), StringComparison.Ordinal);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15),
                $"cancel took {sw.Elapsed} — accept was not aborted promptly");
            return;
        }
    }
}
