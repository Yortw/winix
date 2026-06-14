#nullable enable

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Yort.ShellKit;

namespace Winix.NetCat;

/// <summary>
/// Library entry point for the nc tool: parses arguments, validates option combinations,
/// and dispatches connect / listen / check modes with the supplied byte streams and writer.
/// <c>Program.Main</c> is a thin shell owning console setup and Ctrl+C registration.
/// </summary>
/// <remarks>
/// Stream ownership (seam ADR N2): this method NEVER disposes <paramref name="stdin"/> or
/// <paramref name="stdout"/> — the caller owns their lifetime (production passes the
/// process-lifetime console streams; tests pass MemoryStreams). Check mode's text output
/// (the open-port list) is written through an internal leaveOpen UTF-8 writer over
/// <paramref name="stdout"/>, flushed before return (seam ADR N1) — byte-identical to the
/// previous Console.Out path under ConsoleEnv.UseUtf8Streams.
/// </remarks>
public static class Cli
{
    /// <summary>
    /// Runs the nc CLI.
    /// </summary>
    /// <param name="args">Raw command-line arguments.</param>
    /// <param name="stdin">Bytes to send to the peer (connect/listen modes). Not used by check mode.</param>
    /// <param name="stdout">Receives bytes from the peer (connect/listen) or the open-port
    /// text list (check mode, via the internal writer).</param>
    /// <param name="stderr">Status messages, errors, and the <c>--json</c> summary.</param>
    /// <param name="cancellationToken">Cancellation signal (Ctrl+C in production, owned by Main).
    /// User-cancel surfaces as exit 130 with the "interrupted" line/envelope.</param>
    /// <returns>0 success; 1 refused/DNS/closed; 2 timeout; 125 usage; 126 permission/unexpected; 130 interrupted.</returns>
    public static async Task<int> RunAsync(string[] args, Stream stdin, Stream stdout,
        TextWriter stderr, CancellationToken cancellationToken)
    {
        string version = GetVersion();

        var parser = new CommandLineParser("nc", version)
            .Description("Cross-platform netcat — TCP/UDP send/receive, port checks, TLS clients.")
            .Maturity(ToolMaturity.Core)
            .Flag("--listen", "-l", "Listen for one inbound connection / datagram")
            .Flag("--check", "-z", "Check whether port(s) are open and exit")
            .Flag("--udp", "-u", "Use UDP (default is TCP)")
            .Flag("--tls", "Wrap TCP connection in TLS (client mode only)")
            .FlagAlias("--ssl", "--tls", "true")
            .Flag("--insecure", null, "Skip TLS certificate validation (warning emitted)")
            .Flag("--ipv4", "-4", "Force IPv4")
            .Flag("--ipv6", "-6", "Force IPv6")
            .Flag("--no-shutdown", null, "Don't half-close socket on stdin EOF")
            .Flag("--verbose", "-v", "Show closed/timeout ports too in --check mode")
            .IntOption("--timeout", "-w", "SEC", "Connection / idle timeout in seconds",
                validate: v => v >= 1 && v <= 3600 ? null : "must be between 1 and 3600 seconds")
            .Option("--bind", null, "ADDR", "Bind listener to specific interface (default: all)")
            .StandardFlags()
            .Positional("<host> <port> | <port>")
            .StdinDescription("Bytes to send to the remote (connect/listen modes)")
            .StdoutDescription("Bytes received from the remote, or open ports (check mode)")
            .StderrDescription("Status messages, errors, --json summary")
            .Example("nc -z target.com 443", "Quick port check")
            .Example("nc -z target.com 80,443,5432", "Check multiple ports")
            .Example("nc -z target.com 1-1024 --json", "Range check with JSON output")
            .Example("echo \"GET / HTTP/1.0\" | nc target.com 80", "Send and receive over TCP")
            .Example("nc --tls api.example.com 443", "TLS client")
            .Example("nc -u dnsserver 53 < query.bin", "UDP client")
            .Example("nc -l 8080", "Listen for one TCP connection on :8080")
            .Example("nc target.com 80 < request.bin > response.bin", "File transfer via piping")
            .ComposesWith("xargs", "nc -z target 22,80,443 | xargs -I{} echo open: {}", "Process open-port list")
            .ComposesWith("retry", "retry --until 0 --times 30 --delay 2s nc -z localhost 5432", "Wait until a service port accepts connections (wait-for-it replacement)")
            .JsonField("tool", "string", "Tool name (\"nc\")")
            .JsonField("version", "string", "Tool version")
            .JsonField("mode", "string", "connect | listen | check")
            .JsonField("exit_code", "int", "Tool exit code")
            .JsonField("exit_reason", "string", "Machine-readable exit reason")
            .JsonField("host", "string", "Target host (connect/check modes)")
            .JsonField("port", "int", "Target port (connect/listen modes)")
            .JsonField("protocol", "string", "tcp | udp (connect/listen modes)")
            .JsonField("tls", "bool", "Whether TLS was used (connect mode)")
            .JsonField("remote_address", "string", "Resolved remote endpoint")
            .JsonField("local_address", "string", "Bind endpoint (listen mode)")
            .JsonField("bytes_sent", "int", "Bytes sent to peer")
            .JsonField("bytes_received", "int", "Bytes received from peer")
            .JsonField("duration_ms", "number", "Wall-clock duration (ms)")
            .JsonField("ports", "array", "Per-port results (check mode): port, status, latency_ms?, error?")
            .JsonField("error", "string", "Error summary (only present when the run crashed out of normal exit paths)")
            .ExitCodes(
                (ExitCode.Success, "Success"),
                (1, "Connection refused, DNS failure, bind failure, any closed port (check)"),
                (2, "Timeout"),
                (ExitCode.UsageError, "Usage error"),
                (ExitCode.NotExecutable, "Permission denied (e.g., privileged port)"),
                (130, "Interrupted (Ctrl-C)"));

        ParseResult result = parser.Parse(args);
        if (result.IsHandled) { return result.ExitCode; }
        if (result.HasErrors) { return result.WriteErrors(stderr); }

        // Build options outside the dispatch try so the catch arms can see `options.JsonOutput`
        // and emit a JSON error envelope. Round-3 SFH-I3: without this, a --json consumer whose
        // scan hit an unexpected exception saw a bare stderr crash line + exit 126 with no
        // structured envelope — breaking downstream automation that parses JSON.
        NetCatOptions options;
        try
        {
            options = BuildOptions(result, version);
        }
        catch (UsageException ex)
        {
            stderr.WriteLine(Formatting.FormatErrorLine(ex.Message, useColor: false));
            return ExitCode.UsageError;
        }

        try
        {
            return await RunCoreAsync(options, version, stdin, stdout, stderr, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // User Ctrl+C or linked-timeout OCE that escaped all per-site handlers. The per-site
            // `catch (OCE) when (!ct.IsCancellationRequested)` filters deliberately handle only
            // timeout OCEs; user-cancel is meant to propagate here so the documented exit code
            // 130 actually fires. Round-2 C1 fix: without this arm, Ctrl+C during connect / accept
            // / probe / UDP receive/send fell through to the generic catch-all below and exited
            // 126 "unexpected error" — a contract violation vs --describe/help.
            try { stderr.WriteLine("nc: interrupted"); } catch (IOException) { } catch (ObjectDisposedException) { }
            if (options.JsonOutput)
            {
                TryWriteJson(stderr, () => Formatting.FormatErrorJson(version, options, 130, "interrupted", "user cancelled"));
            }
            return 130;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // Final safety net: TLS/network/filesystem code paths can surface unexpected
            // exception types that don't map to UsageException. Without this, the CLR's
            // default unhandled-exception handler prints a stack trace — and with
            // StackTraceSupport=false in the AOT build the user just sees a cryptic crash.
            // Mirrors retry's Program.Main pattern.
            Exception surface = ExceptionUnwrap.UnwrapTypeInit(ex);
            string msg = string.IsNullOrEmpty(surface.Message)
                ? $"nc: unexpected error: {surface.GetType().Name}"
                : $"nc: unexpected error: {surface.GetType().Name}: {surface.Message}";
            try { stderr.WriteLine(msg); } catch (IOException) { } catch (ObjectDisposedException) { }
            if (options.JsonOutput)
            {
                string err = SafeError.Describe(surface);
                TryWriteJson(stderr, () => Formatting.FormatErrorJson(version, options, ExitCode.NotExecutable, "unexpected_error", err));
            }
            return ExitCode.NotExecutable;
        }
    }

    private static NetCatOptions BuildOptions(ParseResult result, string version)
    {
        bool listen = result.Has("--listen");
        bool check = result.Has("--check");
        bool udp = result.Has("--udp");
        bool tls = result.Has("--tls");
        bool insecure = result.Has("--insecure");
        bool ipv4 = result.Has("--ipv4");
        bool ipv6 = result.Has("--ipv6");
        bool noShutdown = result.Has("--no-shutdown");
        bool verbose = result.Has("--verbose");
        bool json = result.Has("--json");
        bool useColor = result.ResolveColor(checkStdErr: true);
        string? bind = result.Has("--bind") ? result.GetString("--bind") : null;
        TimeSpan timeout = TimeSpan.Zero;
        if (result.Has("--timeout"))
        {
            // --timeout is an .IntOption(validate: 1..3600), so it is int-parsed and
            // range-checked at parse time; GetInt is the same int.Parse(InvariantCulture).
            timeout = TimeSpan.FromSeconds(result.GetInt("--timeout"));
        }

        // Mode mutual exclusion.
        if (listen && check) { throw new UsageException("--listen and --check cannot be used together"); }

        NetCatMode mode = listen ? NetCatMode.Listen : check ? NetCatMode.Check : NetCatMode.Connect;
        NetCatProtocol protocol = udp ? NetCatProtocol.Udp : NetCatProtocol.Tcp;

        // TLS guards.
        if (tls && udp) { throw new UsageException("--tls is not supported with --udp"); }
        if (tls && listen) { throw new UsageException("--tls is not supported with --listen (server certs are not supported in this version)"); }
        if (insecure && !tls) { throw new UsageException("--insecure requires --tls"); }

        // bind only with --listen
        if (bind is not null && !listen) { throw new UsageException("--bind requires --listen"); }

        // Validate --bind as an IP literal now, not silently after the listener uses it.
        // Silent fallback to IPAddress.Any on parse failure is a security foot-gun: a user who
        // types `nc -l --bind 10.0.0..5 8080` (typo) would have had the listener silently bind
        // to all interfaces rather than reject the bad arg. Round-1 I-1 fix. Hostname resolution
        // is deliberately not supported — BSD nc -s only accepts IP literals for the same reason.
        IPAddress? parsedBind = null;
        if (bind is not null && !IPAddress.TryParse(bind, out parsedBind))
        {
            throw new UsageException($"--bind '{bind}' is not a valid IP address (hostnames not supported)");
        }

        // ipv4/ipv6 mutual exclusion
        if (ipv4 && ipv6) { throw new UsageException("--ipv4 and --ipv6 are mutually exclusive"); }

        // Round-3 CR-I1: reject --bind + --ipv4/--ipv6 AF mismatch. Previously
        // `nc --listen --ipv6 --bind 127.0.0.1 8080` parsed cleanly and ResolveBind returned
        // 127.0.0.1 (v4) — silently overriding the user's --ipv6 intent.
        if (parsedBind is not null)
        {
            if (ipv4 && parsedBind.AddressFamily != AddressFamily.InterNetwork)
            {
                throw new UsageException($"--bind '{bind}' is IPv6 but --ipv4 was specified");
            }
            if (ipv6 && parsedBind.AddressFamily != AddressFamily.InterNetworkV6)
            {
                throw new UsageException($"--bind '{bind}' is IPv4 but --ipv6 was specified");
            }
        }

        // verbose only with --check
        if (verbose && !check) { throw new UsageException("--verbose only applies to --check mode"); }

        // no-shutdown only with connect/listen
        if (noShutdown && check) { throw new UsageException("--no-shutdown does not apply to --check mode"); }

        // Positional argument resolution.
        string[] positionals = result.Positionals;
        string? host;
        string portSpec;
        if (mode == NetCatMode.Listen)
        {
            if (positionals.Length != 1) { throw new UsageException("listen mode expects: nc --listen PORT"); }
            host = null;
            portSpec = positionals[0];
        }
        else
        {
            if (positionals.Length != 2) { throw new UsageException("connect/check mode expects: nc HOST PORT"); }
            host = positionals[0];
            portSpec = positionals[1];
        }

        // Wrap PortRangeParser's FormatException as UsageException so Main's catch-arm surfaces
        // it cleanly instead of bubbling to the CLR's unhandled-exception handler. Round-1 C3
        // fix: `nc -z host invalid` previously produced a stack trace + undefined exit code.
        IReadOnlyList<PortRange> ranges;
        try
        {
            ranges = PortRangeParser.Parse(portSpec);
        }
        catch (FormatException ex)
        {
            throw new UsageException(ex.Message);
        }
        if (mode != NetCatMode.Check && (ranges.Count != 1 || ranges[0].Count != 1))
        {
            throw new UsageException("only --check mode accepts port ranges or lists");
        }

        AddressFamily? af = ipv4 ? AddressFamily.InterNetwork : ipv6 ? AddressFamily.InterNetworkV6 : null;

        // Default check timeout = 10s.
        if (mode == NetCatMode.Check && timeout == TimeSpan.Zero)
        {
            timeout = TimeSpan.FromSeconds(10);
        }

        return new NetCatOptions
        {
            Mode = mode,
            Protocol = protocol,
            Host = host,
            Ports = ranges,
            BindAddress = bind,
            UseTls = tls,
            InsecureTls = insecure,
            AddressFamily = af,
            Timeout = timeout,
            NoShutdown = noShutdown,
            Verbose = verbose,
            JsonOutput = json,
            UseColor = useColor,
        };
    }

    private static async Task<int> RunCoreAsync(NetCatOptions options, string version,
        Stream stdin, Stream stdout, TextWriter stderr, CancellationToken cancellationToken)
    {
        switch (options.Mode)
        {
            case NetCatMode.Check:
                {
                    var checker = new PortChecker();
                    // Round-3 C1: pass --ipv4/--ipv6 through so the probe honours the flag.
                    // Previously PortChecker ignored options.AddressFamily — silent contract break.
                    var results = await checker.CheckAsync(options.Host!, options.Ports, options.Timeout, maxConcurrency: 32, cancellationToken, options.AddressFamily).ConfigureAwait(false);

                    // Check mode's open-port text list flows through a leaveOpen UTF-8 writer over
                    // the stdout Stream (seam ADR N1/N2): byte-identical to the old Console.Out path
                    // under ConsoleEnv.UseUtf8Streams, and the caller still owns stdout. Declared
                    // inside the check case so connect/listen never construct it.
                    using var stdoutText = new StreamWriter(stdout, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true);

                    int worstStatus = 0; // 0=open, 1=closed, 2=timeout, 3=error
                    foreach (var r in results)
                    {
                        switch (r.Status)
                        {
                            case PortCheckStatus.Open:
                                // Round-3 CR-I6: suppress the text open-port line under --json so
                                // downstream parsers get pure JSON from a single stream. Matches
                                // the convention used by digest/files/url in the Winix suite.
                                if (!options.JsonOutput)
                                {
                                    stdoutText.WriteLine(Formatting.FormatOpenPortLine(r.Port, options.UseColor));
                                }
                                break;
                            case PortCheckStatus.Closed:
                                if (options.Verbose) { stderr.WriteLine(Formatting.FormatClosedPortLine(r.Port, options.UseColor)); }
                                if (worstStatus < 1) { worstStatus = 1; }
                                break;
                            case PortCheckStatus.Timeout:
                                if (options.Verbose) { stderr.WriteLine(Formatting.FormatTimeoutPortLine(r.Port, options.UseColor)); }
                                if (worstStatus < 2) { worstStatus = 2; }
                                break;
                            case PortCheckStatus.Error:
                                if (options.Verbose) { stderr.WriteLine(Formatting.FormatErrorLine($"{r.Port} {r.ErrorMessage}", options.UseColor)); }
                                if (worstStatus < 3) { worstStatus = 3; }
                                break;
                        }
                    }

                    int errorCount = 0;
                    string? firstErrorMsg = null;
                    foreach (var r in results)
                    {
                        if (r.Status == PortCheckStatus.Error)
                        {
                            errorCount++;
                            firstErrorMsg ??= r.ErrorMessage;
                        }
                    }

                    int exitCode = worstStatus == 0 ? 0 : worstStatus == 2 ? 2 : 1;
                    // Round-2 C3 / round-3 test fix: exit_reason computation extracted to the
                    // library so `some_failed` can be pinned as a direct unit test — process-
                    // spawn tests can't produce a mixed Error+success scan (single host, DNS
                    // errors hit all ports identically).
                    string exitReason = Formatting.ComputeCheckExitReason(results);

                    // Don't exit silently when probes errored in non-verbose plain-text mode.
                    // Round-1 I-5 fix: without this, DNS-failure scans returned exit 1 with NO
                    // stdout/stderr. Round-2 C3 tightens the message: use the total port count as
                    // the denominator (previous wording "all N port probes failed" lied when N was
                    // actually just the error-count, not the scan size).
                    if (!options.JsonOutput && !options.Verbose && worstStatus == 3)
                    {
                        string summary = errorCount == results.Count
                            ? $"all {errorCount} port probes failed"
                            : $"{errorCount} of {results.Count} port probes errored";
                        stderr.WriteLine(Formatting.FormatErrorLine(
                            $"{summary}: {firstErrorMsg ?? "unknown error"} (use --verbose for per-port detail)",
                            options.UseColor));
                    }
                    // Round-3 SFH-I5: all-timeout case was equally silent. `nc -z blackhole 80,443`
                    // against a firewalled host exited 2 with empty stdout AND empty stderr. Only
                    // emit when NO opens are present — if some ports opened they've already been
                    // printed to stdout, so the user knows the scan did something.
                    else if (!options.JsonOutput && !options.Verbose && worstStatus == 2)
                    {
                        int timeoutCount = 0;
                        bool anyOpen = false;
                        foreach (var r in results)
                        {
                            if (r.Status == PortCheckStatus.Open) { anyOpen = true; }
                            else if (r.Status == PortCheckStatus.Timeout) { timeoutCount++; }
                        }
                        if (!anyOpen && timeoutCount > 0)
                        {
                            string summary = timeoutCount == results.Count
                                ? $"all {timeoutCount} port probes timed out"
                                : $"{timeoutCount} of {results.Count} port probes timed out";
                            stderr.WriteLine(Formatting.FormatErrorLine(
                                $"{summary} (use --verbose for per-port detail)", options.UseColor));
                        }
                    }

                    // Flush the open-port text before any JSON envelope so stdout's bytes land in
                    // order relative to the caller's view (seam ADR N1).
                    stdoutText.Flush();

                    if (options.JsonOutput)
                    {
                        // Round-2 I7: JSON summary writer must never mask the real exit code. If
                        // the encoder throws (OOM on a 65k-port scan, writer misuse), log a short
                        // note and still return the computed exit — don't let a diagnostic failure
                        // overwrite the primary result via Main's safety-net.
                        TryWriteJson(stderr, () => Formatting.FormatCheckJson(version, options.Host!, results, exitCode, exitReason));
                    }
                    return exitCode;
                }

            case NetCatMode.Listen:
                {
                    var listener = new NetCatListener();
                    RunResult result = await listener.RunAsync(options, stdin, stdout, stderr, cancellationToken).ConfigureAwait(false);
                    if (options.JsonOutput) { TryWriteJson(stderr, () => Formatting.FormatRunJson(version, options, result)); }
                    return result.ExitCode;
                }

            case NetCatMode.Connect:
            default:
                {
                    var client = new NetCatClient();
                    RunResult result = await client.RunAsync(options, stdin, stdout, stderr, cancellationToken).ConfigureAwait(false);
                    if (options.JsonOutput) { TryWriteJson(stderr, () => Formatting.FormatRunJson(version, options, result)); }
                    return result.ExitCode;
                }
        }
    }

    private static string GetVersion()
    {
        // SDK appends a SourceLink "+gitsha" suffix to AssemblyInformationalVersion
        // by default; strip it so users see plain "X.Y.Z" — matches the convention
        // adopted across clip / digest / ids / schedule / etc.
        string raw = typeof(NetCatOptions).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
        int plus = raw.IndexOf('+');
        return plus >= 0 ? raw.Substring(0, plus) : raw;
    }

    /// <summary>
    /// Writes a JSON summary to stderr, suppressing any exceptions so a diagnostic-path failure
    /// cannot mask the real exit code. Per user's "diagnostic logging must never fail the caller"
    /// rule. Round-2 I7 fix.
    /// </summary>
    private static void TryWriteJson(TextWriter stderr, Func<string> produce)
    {
        try
        {
            stderr.WriteLine(produce());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            try { stderr.WriteLine($"nc: failed to emit JSON summary: {ex.GetType().Name}: {ex.Message}"); }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        }
    }
}
