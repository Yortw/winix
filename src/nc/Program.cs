#nullable enable

using System;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Winix.NetCat;
using Yort.ShellKit;

namespace Nc;

internal sealed class Program
{
    static async Task<int> Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();
        string version = GetVersion();

        var parser = new CommandLineParser("nc", version)
            .Description("Cross-platform netcat — TCP/UDP send/receive, port checks, TLS clients.")
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
            .JsonField("tool", "string", "Tool name (\"nc\")")
            .JsonField("version", "string", "Tool version")
            .JsonField("mode", "string", "connect | listen | check")
            .JsonField("exit_code", "int", "Tool exit code")
            .JsonField("exit_reason", "string", "Machine-readable exit reason")
            .ExitCodes(
                (ExitCode.Success, "Success"),
                (1, "Connection refused, DNS failure, bind failure, any closed port (check)"),
                (2, "Timeout"),
                (ExitCode.UsageError, "Usage error"),
                (ExitCode.NotExecutable, "Permission denied (e.g., privileged port)"),
                (130, "Interrupted (Ctrl-C)"));

        ParseResult result = parser.Parse(args);
        if (result.IsHandled) { return result.ExitCode; }
        if (result.HasErrors) { return result.WriteErrors(Console.Error); }

        // Build options and dispatch.
        try
        {
            NetCatOptions options = BuildOptions(result, version);
            return await DispatchAsync(options, version).ConfigureAwait(false);
        }
        catch (UsageException ex)
        {
            Console.Error.WriteLine(Formatting.FormatErrorLine(ex.Message, useColor: false));
            return ExitCode.UsageError;
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
            int seconds = int.Parse(result.GetString("--timeout")!, CultureInfo.InvariantCulture);
            timeout = TimeSpan.FromSeconds(seconds);
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

        // ipv4/ipv6 mutual exclusion
        if (ipv4 && ipv6) { throw new UsageException("--ipv4 and --ipv6 are mutually exclusive"); }

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

        var ranges = PortRangeParser.Parse(portSpec);
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

    private static async Task<int> DispatchAsync(NetCatOptions options, string version)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        Stream stdin = Console.OpenStandardInput();
        Stream stdout = Console.OpenStandardOutput();
        TextWriter stderr = Console.Error;

        switch (options.Mode)
        {
            case NetCatMode.Check:
                {
                    var checker = new PortChecker();
                    var results = await checker.CheckAsync(options.Host!, options.Ports, options.Timeout, maxConcurrency: 32, cts.Token).ConfigureAwait(false);

                    int worstStatus = 0; // 0=open, 1=closed, 2=timeout, 3=error
                    foreach (var r in results)
                    {
                        switch (r.Status)
                        {
                            case PortCheckStatus.Open:
                                Console.Out.WriteLine(Formatting.FormatOpenPortLine(r.Port, options.UseColor));
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

                    int exitCode = worstStatus == 0 ? 0 : worstStatus == 2 ? 2 : 1;
                    string exitReason = worstStatus switch
                    {
                        0 => "all_open",
                        1 => "some_closed",
                        2 => "some_timeout",
                        _ => "all_failed",
                    };

                    if (options.JsonOutput)
                    {
                        stderr.WriteLine(Formatting.FormatCheckJson(version, options.Host!, results, exitCode, exitReason));
                    }
                    return exitCode;
                }

            case NetCatMode.Listen:
                {
                    var listener = new NetCatListener();
                    RunResult result = await listener.RunAsync(options, stdin, stdout, stderr, cts.Token).ConfigureAwait(false);
                    if (options.JsonOutput) { stderr.WriteLine(Formatting.FormatRunJson(version, options, result)); }
                    return result.ExitCode;
                }

            case NetCatMode.Connect:
            default:
                {
                    var client = new NetCatClient();
                    RunResult result = await client.RunAsync(options, stdin, stdout, stderr, cts.Token).ConfigureAwait(false);
                    if (options.JsonOutput) { stderr.WriteLine(Formatting.FormatRunJson(version, options, result)); }
                    return result.ExitCode;
                }
        }
    }

    private static string GetVersion()
    {
        return typeof(NetCatOptions).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
    }
}

internal sealed class UsageException : System.Exception
{
    public UsageException(string message) : base(message) { }
}
