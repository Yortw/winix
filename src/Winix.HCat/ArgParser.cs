#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Reflection;
using Yort.ShellKit;

namespace Winix.HCat;

/// <summary>
/// Parses argv into <see cref="HCatOptions"/>. Subcommand tool: dispatches on <c>positional[0]</c>
/// (<c>serve</c>/<c>inspect</c>/<c>pipe</c>); a bare invocation serves the current directory; an
/// unknown first positional is a usage error with a hint. Mirrors the schedule/url/qr subcommand
/// precedent. Pure — no I/O. ShellKit prints <c>--help</c>/<c>--version</c>/<c>--describe</c>
/// automatically (signalled via <see cref="Result.IsHandled"/>).
/// </summary>
public static class ArgParser
{
    /// <summary>Parse outcome: <see cref="Success"/> when options parsed cleanly; <see cref="Error"/>
    /// is non-null on usage error; <see cref="IsHandled"/> when ShellKit already emitted
    /// help / version / describe output.</summary>
    /// <param name="Options">The parsed options, or null on error / handled.</param>
    /// <param name="Error">Usage error message, or null on success.</param>
    /// <param name="IsHandled">True when ShellKit already handled the invocation (help/version/describe).</param>
    /// <param name="ExitCode">Exit code appropriate for the handled/error state; 0 on success.</param>
    /// <param name="UseColor">Whether coloured output should be emitted.</param>
    public sealed record Result(
        HCatOptions? Options,
        string? Error,
        bool IsHandled,
        int ExitCode,
        bool UseColor)
    {
        /// <summary>True when options parsed cleanly with no errors and no early-exit handling.</summary>
        public bool Success => Options is not null && Error is null && !IsHandled;
    }

    /// <summary>Parse argv (without the executable name).</summary>
    /// <param name="argv">The raw argument vector.</param>
    /// <returns>A <see cref="Result"/> describing the parse outcome.</returns>
    public static Result Parse(string[] argv)
    {
        // Build a fresh parser per call: ShellKit's CommandLineParser is not reentrant — a shared
        // static instance races under xUnit's parallel collections and misparses flags (trash bug).
        ParseResult parsed = BuildParser().Parse(argv);
        bool useColor = parsed.ResolveColor(checkStdErr: true);

        Result Fail(string error) => new(null, error, false, ExitCode.UsageError, useColor);

        if (parsed.IsHandled)
        {
            return new Result(null, null, true, parsed.ExitCode, useColor);
        }
        if (parsed.HasErrors)
        {
            return Fail(parsed.Errors[0]);
        }

        string[] positionals = parsed.Positionals;

        // Mode dispatch on positional[0]. No positional → serve the current directory.
        HCatMode mode;
        int firstSubArg; // index of the first positional that belongs to the subcommand
        if (positionals.Length == 0)
        {
            mode = HCatMode.Serve;
            firstSubArg = 0;
        }
        else
        {
            switch (positionals[0])
            {
                case "serve":   mode = HCatMode.Serve;   break;
                case "inspect": mode = HCatMode.Inspect; break;
                case "pipe":    mode = HCatMode.Pipe;    break;
                default:
                    return Fail($"unknown subcommand '{positionals[0]}'; did you mean 'hcat serve {positionals[0]}'?");
            }
            firstSubArg = 1;
        }

        // --- shared / global flags ---
        bool lan = parsed.Has("--lan");
        string? host = parsed.Has("--host") ? parsed.GetString("--host") : null;
        bool https = parsed.Has("--https");
        bool json = parsed.Has("--json");

        // --host must be a literal IP. A non-IP value (hostname or typo) used to fall through BindResolver
        // to a silent loopback bind — the user's exposure intent dropped with no error. Reject it here so
        // the failure is visible. Hostname resolution is intentionally out of v1 scope.
        if (host is not null && !IPAddress.TryParse(host, out _))
        {
            return Fail($"--host must be an IP address (got '{host}')");
        }

        // --local is an explicit "loopback only" override: it clears any --lan/--host that came before.
        if (parsed.Has("--local"))
        {
            lan = false;
            host = null;
        }

        // --port (default 8080), validated as an in-range integer.
        int port = 8080;
        if (parsed.Has("--port"))
        {
            string raw = parsed.GetString("--port");
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out port) || port < 1 || port > 65535)
            {
                return Fail($"--port must be an integer between 1 and 65535 (got '{raw}')");
            }
        }

        // --capture <n>: number of requests to capture before exiting (CI). Must be a positive integer.
        int? captureCount = null;
        if (parsed.Has("--capture"))
        {
            string raw = parsed.GetString("--capture");
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < 1)
            {
                return Fail($"--capture must be a positive integer (got '{raw}')");
            }
            captureCount = n;
        }

        // --exit-on <expr>: validate the predicate key at parse time (F13). An unknown key is a usage
        // error rather than a silent never-match that runs the server forever (or exits 1 on --timeout
        // with no hint). ExitOnPredicate.Parse still defends against unknown keys, but we reject first.
        string? exitOn = null;
        if (parsed.Has("--exit-on"))
        {
            string raw = parsed.GetString("--exit-on");
            string? key = ExitOnKey(raw);
            if (key is not ("path" or "method" or "body"))
            {
                return Fail($"--exit-on key must be one of path, method, body (got '{raw}')");
            }
            // Only inspect captures the request body into the record. Serve never reads the body; pipe streams
            // it straight to the child's stdin (Body stays null). So a body~ predicate could never match in
            // serve/pipe — it would run forever, or exit 1 on --timeout, with no hint. Reject at parse time
            // (the F13 rule applied one layer down) rather than ship a silent never-match.
            if (key == "body" && mode != HCatMode.Inspect)
            {
                return Fail("--exit-on body~ is only supported in inspect mode (serve and pipe do not capture the request body); use path= or method=");
            }
            exitOn = raw;
        }

        // --spa / --spa-index are serve-only; --spa-index requires --spa (else it silently does nothing).
        bool spa = parsed.Has("--spa");
        string? spaIndex = parsed.Has("--spa-index") ? parsed.GetString("--spa-index") : null;
        if ((spa || spaIndex is not null) && mode != HCatMode.Serve)
        {
            return Fail("--spa/--spa-index are only valid for serve mode");
        }
        if (spaIndex is not null && !spa)
        {
            return Fail("--spa-index requires --spa");
        }
        // --spa-index is a bare filename resolved under the served root. Reject path components, traversal, and
        // rooted/absolute values at parse time — clearer than a silent 404, and it keeps the shell strictly
        // inside the served tree (so the startup-warning's Path.Combine check is also safe).
        if (spaIndex is not null
            && (spaIndex.IndexOf('/') >= 0 || spaIndex.IndexOf('\\') >= 0 || System.IO.Path.IsPathRooted(spaIndex)))
        {
            return Fail("--spa-index must be a bare filename (no path components)");
        }

        // --timeout <dur>: parse with ShellKit's DurationParser (suffix required: ms/s/m/h/d/w).
        TimeSpan? timeout = null;
        if (parsed.Has("--timeout"))
        {
            string raw = parsed.GetString("--timeout");
            if (!DurationParser.TryParse(raw, out TimeSpan t))
            {
                return Fail($"--timeout must be a duration like 30s, 5m, 1h (got '{raw}')");
            }
            timeout = t;
        }

        // --- per-mode shaping ---
        var options = new HCatOptions
        {
            Mode = mode,
            Port = port,
            Lan = lan,
            Host = host,
            Https = https,
            Json = json,
            UseColor = useColor,
            CaptureCount = captureCount,
            ExitOn = exitOn,
            Timeout = timeout,
        };

        switch (mode)
        {
            case HCatMode.Serve:
            {
                // Optional [dir] positional after the (optional) subcommand token; defaults to ".".
                string directory = ".";
                if (positionals.Length > firstSubArg)
                {
                    directory = positionals[firstSubArg];
                }
                bool upload = parsed.Has("--upload");
                string? uploadDir = parsed.Has("--upload-dir") ? parsed.GetString("--upload-dir") : null;
                options = options with
                {
                    Directory = directory,
                    Upload = upload,
                    UploadDir = uploadDir,
                    Spa = spa,
                    SpaIndexFile = spaIndex ?? "index.html",
                };
                break;
            }

            case HCatMode.Inspect:
            {
                int inspectStatus = 200;
                if (parsed.Has("--status"))
                {
                    string raw = parsed.GetString("--status");
                    if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out inspectStatus)
                        || inspectStatus < 100 || inspectStatus > 599)
                    {
                        return Fail($"--status must be an HTTP status code between 100 and 599 (got '{raw}')");
                    }
                }
                options = options with { InspectStatus = inspectStatus };
                break;
            }

            case HCatMode.Pipe:
            {
                // Everything after the `pipe` subcommand (and the `--` separator, which ShellKit
                // collapses into the positional list) is the child command. Require ≥1 token.
                var pipeCommand = new List<string>();
                for (int i = firstSubArg; i < positionals.Length; i++)
                {
                    pipeCommand.Add(positionals[i]);
                }
                if (pipeCommand.Count == 0)
                {
                    return Fail("pipe requires a command after '--' (e.g. hcat pipe -- jq .)");
                }
                options = options with { PipeCommand = pipeCommand };
                break;
            }
        }

        return new Result(options, null, false, 0, useColor);
    }

    /// <summary>Extracts the lowercased left-hand key of an <c>--exit-on</c> expression
    /// (<c>path</c>/<c>method</c> before <c>=</c>, or <c>body</c> before <c>~</c>), or null when the
    /// expression is blank or has no separator. Mirrors the separator-precedence rule in
    /// <see cref="ExitOnPredicate.Parse"/> so the parser's validation and the predicate's matching agree.</summary>
    private static string? ExitOnKey(string expr)
    {
        if (string.IsNullOrWhiteSpace(expr)) { return null; }

        int eq = expr.IndexOf('=');
        int tilde = expr.IndexOf('~');

        // Whichever separator appears first determines the key (matches ExitOnPredicate.Parse).
        if (tilde >= 0 && (eq < 0 || tilde < eq))
        {
            return expr.Substring(0, tilde).Trim().ToLowerInvariant();
        }
        if (eq >= 0)
        {
            return expr.Substring(0, eq).Trim().ToLowerInvariant();
        }
        // No separator at all (e.g. "garbage") — not a valid predicate.
        return null;
    }

    private static CommandLineParser BuildParser()
    {
        return new CommandLineParser("hcat", ResolveVersion())
            .Description("Instant HTTP server: serve a folder, catch incoming requests, or pipe a command over HTTP.")
            .StandardFlags()
            // --json is already registered by StandardFlags(); do NOT re-add it. Read via parsed.Has("--json").
            .Platform("cross-platform",
                replaces: new[] { "python -m http.server", "npx http-server", "ncat -l", "webhook.site" },
                valueOnWindows: "Windows has no built-in one-line HTTP server; this is a single AOT binary, no Python/Node runtime.",
                valueOnUnix: "One binary covering serve / inspect / pipe, with LAN-share QR codes and CI stop conditions built in.")
            .ExitCodes(
                (0, "Success (clean shutdown, or a CI stop condition was met)"),
                (1, "A CI stop condition (--capture/--exit-on) was not met before --timeout elapsed"),
                (ExitCode.UsageError, "Usage error: unknown subcommand/flag, bad --exit-on key, non-integer port/status/capture"),
                (ExitCode.NotExecutable, "Startup failure: could not bind the port, or the self-signed certificate could not be created"))
            .Flag("--lan", "Bind 0.0.0.0 to share on the local network (prints a QR code).")
            .Flag("--local", "Force loopback-only binding (overrides --lan/--host).")
            .Flag("--https", "Enable TLS with an in-memory self-signed certificate.")
            .Flag("--upload", "(serve) Enable the POST upload receiver.")
            .Flag("--spa", "(serve) SPA fallback: unmatched browser navigations return the index file.")
            .Option("--spa-index", null, "FILE", "(serve, with --spa) SPA fallback filename (default index.html).")
            .Option("--host", null, "ADDR", "Explicit bind address.")
            .IntOption("--port", null, "N", "Listen port (default 8080).")
            .Option("--upload-dir", null, "DIR", "(serve) Upload target directory (default ./uploads).")
            .IntOption("--status", null, "CODE", "(inspect) HTTP status to respond with (default 200).")
            .Option("--capture", null, "N", "(CI) Exit after capturing N requests.")
            .Option("--exit-on", null, "EXPR", "(CI) Exit when a request matches: path=/x, method=POST, body~text (body~ is inspect-only).")
            .Option("--timeout", null, "DUR", "(CI) Fail if the stop condition is not met within DUR (e.g. 30s, 5m).")
            .Positional("[serve|inspect|pipe] [dir] [-- command...]")
            .StdinDescription("Not used.")
            .StdoutDescription("Plain: human request log to stderr; --json: JSONL to stdout — request records (inspect/pipe) or {method,path,status} access-log lines (serve).")
            .StderrDescription("Bind banner, request log, and errors.")
            .Example("hcat", "Serve the current directory (localhost)")
            .Example("hcat serve ./public --lan", "Share a folder on your LAN (QR printed)")
            .Example("hcat inspect --lan", "Catch and print incoming webhooks")
            .Example("hcat pipe -- jq .", "Expose jq over HTTP");
    }

    // Read AssemblyInformationalVersion (injected via /p:Version by the release pipeline) and strip
    // the "+gitsha" SourceLink suffix. Falls back to AssemblyVersion for dev builds. Matches trash.
    private static string ResolveVersion()
    {
        string? info = typeof(ArgParser).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(info))
        {
            int plus = info.IndexOf('+');
            return plus >= 0 ? info.Substring(0, plus) : info;
        }
        return typeof(ArgParser).Assembly.GetName().Version?.ToString() ?? "0.0.0";
    }
}
