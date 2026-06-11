#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Yort.ShellKit;

namespace Winix.Online;

/// <summary>
/// Internal bundle of injectable seams. <see langword="null"/> members fall back to the production
/// implementations built in <see cref="Cli.RunAsync(string[], TextWriter, TextWriter, CancellationToken, OnlineSeams?)"/>.
/// Exists for testability only — NOT a generalisation runway (ADR D10).
/// </summary>
internal sealed record OnlineSeams(
    Func<bool>? RouteAvailable = null,
    DnsProbe? DnsProbe = null,
    HttpProbe? HttpProbe = null,
    Func<IReadOnlyList<string>, IReadOnlyList<string>>? EndpointOrder = null,
    Func<DateTimeOffset>? Now = null,
    Func<TimeSpan, CancellationToken, Task>? Sleep = null);

/// <summary>
/// Library entry point for the online tool: parses arguments, validates, builds checks, runs the
/// wait loop, and routes output. <c>Program.Main</c> is a thin shell owning console setup and Ctrl+C.
/// </summary>
public static class Cli
{
    private const int ExitReady = 0;
    private const int ExitNotReadyOnce = 1;
    private const int ExitTimedOut = 124;        // GNU timeout(1) convention
    private const int ExitUnexpected = ExitCode.NotExecutable; // 126 — tool fault, distinct from usage 125

    /// <summary>Production entry point. Builds the real network/clock seams.</summary>
    public static Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr, CancellationToken cancellationToken)
        => RunAsync(args, stdout, stderr, cancellationToken, seams: null);

    /// <summary>
    /// Test/production entry point. When <paramref name="seams"/> (or any member) is null the real
    /// implementation is used.
    /// </summary>
    internal static async Task<int> RunAsync(
        string[] args, TextWriter stdout, TextWriter stderr, CancellationToken cancellationToken, OnlineSeams? seams)
    {
        string version = GetVersion();

        var parser = new CommandLineParser("online", version)
            .Description("Block until the internet — or a named endpoint — is actually healthy.")
            .Maturity(ToolMaturity.Fresh)
            .Flag("--internet", null, "Wait for working internet (layered, captive-portal-aware). Default when no check flag is given.")
            .ListOption("--url", null, "URL", "Wait until URL returns a status matching --status (repeatable)")
            .ListOption("--endpoint", null, "URL", "Override the built-in 204 connectivity endpoints for --internet (repeatable)")
            .Option("--status", null, "SPEC", "Expected status for --url: 2xx (default), list 200,204, or range 200-299")
            .Option("--timeout", null, "DURATION", "Total wait budget, e.g. 30s, 10m. 0 = forever (default: 10m)")
            .Option("--interval", null, "DURATION", "Sleep between poll cycles (default: 2s)")
            .Option("--probe-timeout", null, "DURATION", "Per-probe DNS/HTTP timeout (default: 3s)")
            .Flag("--once", null, "Run one cycle and exit (no waiting): exit 0 ready, 1 not ready")
            .Flag("--verbose", "-v", "Print per-attempt diagnostics to stderr")
            .StandardFlags()
            .ExitCodes(
                (ExitReady, "Ready — every requested check healthy"),
                (ExitNotReadyOnce, "--once only: checked once, not ready right now"),
                (ExitTimedOut, "Timed out before ready (wait mode)"),
                (ExitCode.UsageError, "Usage error: bad arguments, unparseable duration/status, malformed URL"),
                (ExitUnexpected, "Unexpected error (tool fault)"),
                (130, "Interrupted (Ctrl+C)"))
            .Platform("cross-platform",
                replaces: new[] { "wait-for-it.sh", "wait-on" },
                valueOnWindows: "No native 'is the internet up' wait; PowerShell scripting required, and Test-Connection is ICMP (portal-blind)",
                valueOnUnix: "Captive-portal-aware connectivity gate without a Node runtime or bash boilerplate")
            .StdinDescription("Not used")
            .StdoutDescription("--json envelope (own-data tool); otherwise empty")
            .StderrDescription("Human summary and per-attempt verbose lines")
            .Example("online", "Wait up to 10m for working internet")
            .Example("online --once", "Is the internet up right now? (exit 0/1)")
            .Example("online --internet --url https://api/health", "Network back AND my server healthy")
            .Example("online --url https://x --status 200,204", "Wait for an exact status set")
            .ComposesWith("retry", "online && retry --times 3 dotnet test", "Resume work once the network is back")
            .JsonField("tool", "string", "Tool name (\"online\")")
            .JsonField("version", "string", "Tool version")
            .JsonField("ready", "bool", "Whether every requested check passed")
            .JsonField("timed_out", "bool", "Whether the wait budget was exhausted")
            .JsonField("elapsed_ms", "int", "Wall time in milliseconds")
            .JsonField("attempts", "int", "Poll cycles run")
            .JsonField("checks", "object[]", "Per-check results: { kind, target, ok, detail }");

        ParseResult result = parser.Parse(args);
        if (result.IsHandled) { return result.ExitCode; }
        if (result.HasErrors) { return result.WriteErrors(stderr); }

        // --- Build options (validation → 125 on any failure) ---
        OnlineOptions? options = BuildOptions(result, out string? optionError);
        if (options is null)
        {
            return result.WriteError(optionError ?? "invalid arguments", stderr);
        }

        bool jsonOutput = result.Has("--json");
        // Summary/verbose go to stderr; the JSON envelope is the only thing on stdout.
        bool useColor = result.ResolveColor(checkStdErr: true);

        try
        {
            return await RunWait(options, version, jsonOutput, useColor, stdout, stderr, seams, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // User Ctrl+C — conventional interrupted exit.
            SafeWriteLine(stderr, "online: interrupted");
            return 130;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // Final safety net — never leak a framework stack trace / SR resource key to the user.
            string msg = SafeError.Describe(ex);
            SafeWriteLine(stderr, $"online: unexpected error: {ex.GetType().Name}: {msg}");
            return ExitUnexpected;
        }
    }

    private static async Task<int> RunWait(
        OnlineOptions options, string version, bool jsonOutput, bool useColor,
        TextWriter stdout, TextWriter stderr, OnlineSeams? seams, CancellationToken cancellationToken)
    {
        // Build production seams when not injected. One SocketsHttpHandler/HttpClient is shared
        // across all probes in this run (connection reuse across poll cycles). AllowAutoRedirect is
        // OFF: a captive portal's 302→login must be visible as a non-204, not silently followed to a
        // 200. The same client serves --url, so a redirecting health URL simply won't match 2xx
        // (keeps waiting) — documented behaviour, diagnosable with -v.
        using var handler = new SocketsHttpHandler { AllowAutoRedirect = false };
        using var http = new HttpClient(handler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

        Func<bool> route = seams?.RouteAvailable ?? NetworkInterface.GetIsNetworkAvailable;
        DnsProbe dns = seams?.DnsProbe ?? RealDnsProbe;
        HttpProbe httpProbe = seams?.HttpProbe ?? ((url, ct) => RealHttpProbe(http, url, options.ProbeTimeout, ct));
        Func<IReadOnlyList<string>, IReadOnlyList<string>> order = seams?.EndpointOrder ?? Shuffle;
        Func<DateTimeOffset> now = seams?.Now ?? (() => DateTimeOffset.UtcNow);
        Func<TimeSpan, CancellationToken, Task> sleep = seams?.Sleep ?? ((d, ct) => Task.Delay(d, ct));

        var checks = new List<IReadinessCheck>();
        if (options.CheckInternet)
        {
            checks.Add(new InternetCheck(options.Endpoints, route, dns, httpProbe, order));
        }
        foreach (string url in options.Urls)
        {
            checks.Add(new UrlCheck(url, options.Status, httpProbe));
        }

        Action<int, IReadOnlyList<CheckResult>>? onAttempt = null;
        // Verbose per-attempt lines go to stderr; safe to emit even in --json mode (JSON is on stdout).
        if (options.Verbose)
        {
            onAttempt = (attempt, results) => SafeWriteLine(stderr, Formatting.FormatAttempt(attempt, results, useColor));
        }

        var engine = new WaitEngine(now, sleep);
        WaitResult waitResult = await engine.RunAsync(checks, options, onAttempt, cancellationToken);

        if (jsonOutput)
        {
            SafeWriteLine(stdout, Formatting.FormatJson(waitResult, version));
        }
        else
        {
            SafeWriteLine(stderr, Formatting.FormatSummary(waitResult, useColor));
        }

        return waitResult.Outcome switch
        {
            WaitOutcome.Ready => ExitReady,
            WaitOutcome.TimedOut => ExitTimedOut,
            WaitOutcome.NotReady => ExitNotReadyOnce,
            _ => ExitNotReadyOnce,
        };
    }

    /// <summary>Parses and validates raw args into <see cref="OnlineOptions"/>; null + message on error.</summary>
    private static OnlineOptions? BuildOptions(ParseResult result, out string? error)
    {
        error = null;

        // --status
        StatusSpec status = StatusSpec.Default;
        if (result.Has("--status"))
        {
            if (!StatusSpec.TryParse(result.GetString("--status"), out status, out string? statusError))
            {
                error = statusError ?? "invalid --status";
                return null;
            }
        }

        // --url (repeatable) — each must be an absolute http(s) URL.
        string[] urls = result.GetList("--url");
        foreach (string url in urls)
        {
            if (!IsHttpUrl(url))
            {
                error = $"invalid --url value: '{url}' (must be an absolute http/https URL)";
                return null;
            }
        }

        // --endpoint (repeatable) override, else the built-in 204 list.
        string[] endpointOverride = result.GetList("--endpoint");
        foreach (string ep in endpointOverride)
        {
            if (!IsHttpUrl(ep))
            {
                error = $"invalid --endpoint value: '{ep}' (must be an absolute http/https URL)";
                return null;
            }
        }
        IReadOnlyList<string> endpoints = endpointOverride.Length > 0 ? endpointOverride : DefaultEndpoints.All;

        // Bare online (no --internet, no --url) defaults to --internet.
        bool checkInternet = result.Has("--internet") || urls.Length == 0;

        // Reject silent no-op flags (review F4): a flag that would be parsed but never take effect is
        // worse than a clean usage error — it masks a misconfiguration (the "--color no-op" defect class).
        if (endpointOverride.Length > 0 && !checkInternet)
        {
            error = "--endpoint requires the internet check (it overrides the connectivity endpoints); "
                  + "add --internet, or drop --endpoint if you only meant --url checks.";
            return null;
        }
        if (result.Has("--status") && urls.Length == 0)
        {
            error = "--status only applies to --url checks, but no --url was given.";
            return null;
        }

        // --timeout (0 = infinite sentinel TimeSpan.Zero)
        TimeSpan timeout = TimeSpan.FromMinutes(10);
        if (result.Has("--timeout"))
        {
            if (!TryParseTimeout(result.GetString("--timeout"), out timeout))
            {
                error = $"invalid --timeout value: '{result.GetString("--timeout")}' (e.g. 30s, 10m, or 0 for forever)";
                return null;
            }
        }

        // --interval (> 0)
        TimeSpan interval = TimeSpan.FromSeconds(2);
        if (result.Has("--interval"))
        {
            if (!DurationParser.TryParse(result.GetString("--interval"), out interval) || interval <= TimeSpan.Zero)
            {
                error = $"invalid --interval value: '{result.GetString("--interval")}' (must be a positive duration, e.g. 2s)";
                return null;
            }
        }

        // --probe-timeout (> 0)
        TimeSpan probeTimeout = TimeSpan.FromSeconds(3);
        if (result.Has("--probe-timeout"))
        {
            if (!DurationParser.TryParse(result.GetString("--probe-timeout"), out probeTimeout) || probeTimeout <= TimeSpan.Zero)
            {
                error = $"invalid --probe-timeout value: '{result.GetString("--probe-timeout")}' (must be a positive duration, e.g. 3s)";
                return null;
            }
        }

        return new OnlineOptions(
            checkInternet, urls, status, endpoints,
            timeout, interval, probeTimeout,
            once: result.Has("--once"),
            verbose: result.Has("--verbose"));
    }

    /// <summary>Parses a timeout, accepting the literal <c>0</c> as the infinite sentinel
    /// (<see cref="TimeSpan.Zero"/>). Any other value goes through <see cref="DurationParser"/>
    /// and must be positive.</summary>
    private static bool TryParseTimeout(string text, out TimeSpan value)
    {
        if (text.Trim() == "0")
        {
            value = TimeSpan.Zero;   // infinite
            return true;
        }
        if (DurationParser.TryParse(text, out value) && value > TimeSpan.Zero)
        {
            return true;
        }
        value = TimeSpan.Zero;
        return false;
    }

    private static bool IsHttpUrl(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    // ── Production seam implementations ──────────────────────────────────────────────

    private static async Task<bool> RealDnsProbe(string host, CancellationToken cancellationToken)
    {
        try
        {
            IPAddress[] addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
            return addresses.Length > 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;   // malformed host
        }
    }

    private static async Task<HttpProbeResult> RealHttpProbe(HttpClient http, string url, TimeSpan probeTimeout, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(probeTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            // ResponseHeadersRead: we need only the status line, never the body (the 204 STATUS is the
            // portal discriminator — review F3/F9). Not reading the body avoids a per-cycle full-body
            // allocation and the false-negative risk of a byte injected into a 204. Disposing the
            // response aborts the unread connection, which is fine for a probe.
            using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
            return HttpProbeResult.Reached((int)response.StatusCode);
        }
        // Cancel disambiguation (review F1): only rethrow when the OUTER (user) token is cancelled —
        // in which case exit 130 is correct regardless of which linked token actually fired the OCE.
        // If only the per-probe timeout fired, the outer token is NOT cancelled, the guard is false,
        // and we fall through to return Unreachable. There is no path where a probe timeout with no
        // user-cancel rethrows as a user-cancel.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;   // user cancel — abort the wait
        }
        catch (OperationCanceledException)
        {
            return HttpProbeResult.Unreachable;   // per-probe timeout
        }
        catch (HttpRequestException)
        {
            return HttpProbeResult.Unreachable;   // connect/TLS/DNS-at-request failure
        }
    }

    private static IReadOnlyList<string> Shuffle(IReadOnlyList<string> items)
    {
        string[] arr = items.ToArray();
        // Fisher–Yates with the shared RNG. Randomised order is fed via this seam so tests stay
        // deterministic (they inject identity); Random never appears in the test path (ADR D6/D10).
        for (int i = arr.Length - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (arr[i], arr[j]) = (arr[j], arr[i]);
        }
        return arr;
    }

    private static void SafeWriteLine(TextWriter writer, string message)
    {
        try { writer.WriteLine(message); }
        catch (IOException) { /* downstream pipe closed */ }
        catch (ObjectDisposedException) { /* writer disposed */ }
    }

    private static string GetVersion()
    {
        string raw = typeof(Cli).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
        int plus = raw.IndexOf('+');
        return plus >= 0 ? raw.Substring(0, plus) : raw;
    }
}
