#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Yort.ShellKit;

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
    /// <param name="addressFamily">
    /// Optional address-family pin. When set (i.e. user passed <c>--ipv4</c> / <c>--ipv6</c>),
    /// DNS resolution is filtered to that family and the socket is constructed with it.
    /// Null = let the resolver + OS pick (legacy default).
    /// </param>
    public async Task<IReadOnlyList<PortCheckResult>> CheckAsync(
        string host,
        IReadOnlyList<PortRange> ranges,
        TimeSpan timeout,
        int maxConcurrency,
        CancellationToken ct,
        AddressFamily? addressFamily = null)
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
                    results[idx] = await ProbeOneAsync(host, port, timeout, addressFamily, ct).ConfigureAwait(false);
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

    private static async Task<PortCheckResult> ProbeOneAsync(string host, int port, TimeSpan timeout, AddressFamily? addressFamily, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout > TimeSpan.Zero)
        {
            probeCts.CancelAfter(timeout);
        }

        // When the user pinned the AF with --ipv4/--ipv6, resolve ourselves and filter by
        // family so the probe honours the flag. The default TcpClient() ctor uses
        // AddressFamily.Unknown and the dual-stack resolver picks arbitrarily. Round-3 C1 fix.
        IPAddress? resolvedAddress = null;
        if (addressFamily is AddressFamily af)
        {
            IPAddress[] addresses;
            try
            {
                addresses = await Dns.GetHostAddressesAsync(host, af, probeCts.Token).ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                return PortCheckResult.Error(port, ex.Message);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return PortCheckResult.Timeout(port);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException and not StackOverflowException)
            {
                return PortCheckResult.Error(port, SafeError.Describe(ex));
            }
            if (addresses.Length == 0)
            {
                string family = af == AddressFamily.InterNetwork ? "IPv4" : "IPv6";
                return PortCheckResult.Error(port, $"no {family} address for host");
            }
            resolvedAddress = addresses[0];
        }

        using TcpClient client = addressFamily is AddressFamily af2 ? new TcpClient(af2) : new TcpClient();
        try
        {
            if (resolvedAddress is not null)
            {
                await client.ConnectAsync(resolvedAddress, port, probeCts.Token).ConfigureAwait(false);
            }
            else
            {
                await client.ConnectAsync(host, port, probeCts.Token).ConfigureAwait(false);
            }
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
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException and not StackOverflowException)
        {
            // Any other exception from ConnectAsync (ArgumentException on malformed hostname,
            // NotSupportedException on bad AddressFamily, etc.) must NOT abort the entire scan —
            // Task.WhenAll would re-throw it, losing the other 1023 port results. Classify as
            // Error so the per-port scan completes. Round-1 I-4 fix.
            return PortCheckResult.Error(port, SafeError.Describe(ex));
        }
    }
}
