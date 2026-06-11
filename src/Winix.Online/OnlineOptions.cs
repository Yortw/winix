#nullable enable

using System;
using System.Collections.Generic;

namespace Winix.Online;

/// <summary>
/// Validated, parsed configuration for one <c>online</c> invocation. Raw-string parsing and
/// validation live in <c>Cli</c>; this type only holds already-valid values.
/// </summary>
public sealed class OnlineOptions
{
    /// <summary>Whether the layered internet check runs (true for bare <c>online</c>).</summary>
    public bool CheckInternet { get; }

    /// <summary>Named-URL health-wait targets (validated absolute http(s) URLs).</summary>
    public IReadOnlyList<string> Urls { get; }

    /// <summary>Expected-status matcher for <c>--url</c> checks.</summary>
    public StatusSpec Status { get; }

    /// <summary>Connectivity endpoints for the internet check (override or <see cref="DefaultEndpoints"/>).</summary>
    public IReadOnlyList<string> Endpoints { get; }

    /// <summary>Total wait budget. <see cref="TimeSpan.Zero"/> means wait forever.</summary>
    public TimeSpan Timeout { get; }

    /// <summary>Sleep between poll cycles.</summary>
    public TimeSpan Interval { get; }

    /// <summary>Per-probe (DNS / HTTP) timeout.</summary>
    public TimeSpan ProbeTimeout { get; }

    /// <summary>Run exactly one cycle and exit (no waiting).</summary>
    public bool Once { get; }

    /// <summary>Emit per-attempt diagnostics to stderr.</summary>
    public bool Verbose { get; }

    /// <summary>Creates a validated options object.</summary>
    public OnlineOptions(
        bool checkInternet,
        IReadOnlyList<string> urls,
        StatusSpec status,
        IReadOnlyList<string> endpoints,
        TimeSpan timeout,
        TimeSpan interval,
        TimeSpan probeTimeout,
        bool once,
        bool verbose)
    {
        CheckInternet = checkInternet;
        Urls = urls;
        Status = status;
        Endpoints = endpoints;
        Timeout = timeout;
        Interval = interval;
        ProbeTimeout = probeTimeout;
        Once = once;
        Verbose = verbose;
    }
}
