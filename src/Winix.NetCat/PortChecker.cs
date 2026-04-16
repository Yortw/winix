#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Winix.NetCat;

/// <summary>
/// Probes one or more TCP ports for openness. Used by the <c>--check</c> mode.
/// Each probe opens a TCP connection then immediately closes it.
/// </summary>
public sealed class PortChecker
{
    /// <summary>
    /// Probes every port in <paramref name="ranges"/> against <paramref name="host"/>
    /// concurrently (capped by <paramref name="maxConcurrency"/>) and returns the
    /// per-port results in input order.
    /// </summary>
    /// <param name="host">Target hostname or IP.</param>
    /// <param name="ranges">Port ranges to probe (flattened).</param>
    /// <param name="timeout">Per-port connect timeout.</param>
    /// <param name="maxConcurrency">Maximum simultaneous in-flight probes.</param>
    /// <param name="ct">Cancellation token (e.g. Ctrl-C).</param>
    public async Task<IReadOnlyList<PortCheckResult>> CheckAsync(
        string host,
        IReadOnlyList<PortRange> ranges,
        TimeSpan timeout,
        int maxConcurrency,
        CancellationToken ct)
    {
        var allPorts = new List<int>();
        foreach (PortRange range in ranges)
        {
            foreach (int port in range.Enumerate())
            {
                allPorts.Add(port);
            }
        }

        var results = new PortCheckResult[allPorts.Count];
        using var throttle = new SemaphoreSlim(maxConcurrency);

        var tasks = new Task[allPorts.Count];
        for (int i = 0; i < allPorts.Count; i++)
        {
            int idx = i;
            int port = allPorts[i];
            tasks[i] = Task.Run(async () =>
            {
                await throttle.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    results[idx] = await ProbeOneAsync(host, port, timeout, ct).ConfigureAwait(false);
                }
                finally
                {
                    throttle.Release();
                }
            }, ct);
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
    }

    private static async Task<PortCheckResult> ProbeOneAsync(string host, int port, TimeSpan timeout, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        using var client = new TcpClient();
        using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout > TimeSpan.Zero)
        {
            probeCts.CancelAfter(timeout);
        }

        try
        {
            await client.ConnectAsync(host, port, probeCts.Token).ConfigureAwait(false);
            sw.Stop();
            return PortCheckResult.Open(port, sw.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return PortCheckResult.Timeout(port);
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused)
        {
            return PortCheckResult.Closed(port);
        }
        catch (SocketException ex)
        {
            return PortCheckResult.Error(port, ex.Message);
        }
    }
}
