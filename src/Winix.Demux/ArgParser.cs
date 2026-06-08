#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Yort.ShellKit;

namespace Winix.Demux;

/// <summary>
/// Parses demux arguments. Route flags (<c>--to</c>/<c>--exec</c>/<c>--default-to</c>/<c>--default-exec</c>)
/// carry their operands and are peeled out of argv by a custom pre-pass before the residual
/// (standard flags plus <c>--field</c>, <c>--delimiter</c>, <c>--all</c>, <c>--append</c>,
/// <c>--exit-on-child-error</c>) is handed to ShellKit's <see cref="CommandLineParser"/>.
/// </summary>
public static class ArgParser
{
    /// <summary>
    /// Parses <paramref name="args"/> using the pre-built <paramref name="parser"/> for the
    /// residual flags.
    /// </summary>
    /// <param name="args">Raw command-line arguments.</param>
    /// <param name="parser">Parser built by <see cref="BuildParser"/>; pass the real version string.</param>
    /// <param name="stderr">Writer for usage-error messages.</param>
    /// <param name="opts">Populated on success (exit code 0); null on any error or handled result.</param>
    /// <param name="handled">
    /// Set to <see langword="true"/> when the parser handled <c>--help</c>, <c>--version</c>, or
    /// <c>--describe</c> (output already written to stdout); the caller should return
    /// <see cref="ParseResult.ExitCode"/> directly.
    /// </param>
    /// <returns>
    /// 0 on success; <see cref="ExitCode.UsageError"/> (125) on any usage error;
    /// the handled exit code when <paramref name="handled"/> is <see langword="true"/>.
    /// </returns>
    public static int TryParse(
        string[] args,
        CommandLineParser parser,
        TextWriter stderr,
        out DemuxOptions? opts,
        out bool handled)
    {
        opts = null;
        handled = false;

        if (!ScanRoutes(args, out List<RawRoute> rawRoutes, out RawRoute? rawDefault,
                        out List<string> residual, out string? scanError))
        {
            stderr.WriteLine($"demux: {scanError}");
            return ExitCode.UsageError;
        }

        ParseResult result = parser.Parse(residual.ToArray());
        if (result.IsHandled)
        {
            handled = true;
            return result.ExitCode;
        }

        if (result.HasErrors)
        {
            return result.WriteErrors(stderr);
        }

        if (rawRoutes.Count == 0)
        {
            stderr.WriteLine("demux: no routes — give at least one --to PATTERN FILE or --exec PATTERN CMD");
            return ExitCode.UsageError;
        }

        // --field is registered as IntOption so ShellKit already validated the value at parse time.
        int? field = result.Has("--field") ? result.GetInt("--field") : null;

        // F10: an explicitly-empty --delimiter collides with the ""=whitespace sentinel; reject it.
        if (result.Has("--delimiter") && result.GetString("--delimiter").Length == 0)
        {
            stderr.WriteLine("demux: --delimiter must not be empty");
            return ExitCode.UsageError;
        }

        var routes = new List<RouteSpec>(rawRoutes.Count);
        foreach (RawRoute r in rawRoutes)
        {
            if (!TryCompile(r.Pattern, stderr, out Regex? rx))
            {
                return ExitCode.UsageError;
            }

            routes.Add(new RouteSpec(rx!, r.Pattern, r.Kind, r.Target));
        }

        RouteSpec? defaultRoute = rawDefault is RawRoute d
            ? RouteSpec.Default(d.Kind, d.Target)
            : null;

        opts = new DemuxOptions(
            routes,
            defaultRoute,
            field,
            result.Has("--delimiter") ? result.GetString("--delimiter") : "",
            result.Has("--all"),
            result.Has("--append"),
            result.Has("--exit-on-child-error"),
            result.Has("--json"),
            result.ResolveColor(checkStdErr: true));

        return 0;
    }

    /// <summary>
    /// Builds the <see cref="CommandLineParser"/> for the residual flags and documents the
    /// manually-parsed route flags in <c>--help</c> / <c>--describe</c> output.
    /// </summary>
    /// <param name="version">Tool version string (from assembly or build props).</param>
    public static CommandLineParser BuildParser(string version)
    {
        return new CommandLineParser("demux", version)
            .Description("Route each line of stdin to files or commands by regex; unmatched lines pass through to stdout.")
            .Maturity(ToolMaturity.Fresh)
            .PreferDefaultWhen(
                "single-sink filtering by pattern — use grep or Select-String",
                "transforming lines (substitutions, field extraction) — use awk or sed",
                "copying one stream to N identical sinks — use tee")
            .StandardFlags()
            // IntOption so ShellKit validates the value as an integer at parse time; non-integer
            // input surfaces via HasErrors rather than throwing from GetInt.
            .IntOption("--field", null, "N", "Test the regex against column N (1-based) instead of the whole line",
                validate: v => v < 1 ? "must be >= 1" : null)
            .Option("--delimiter", null, "CHAR", "Field delimiter (default: runs of whitespace)")
            .Flag("--all", null, "Broadcast: route to every matching route (default: first-match)")
            .Flag("--append", null, "File targets append instead of truncate")
            .Flag("--exit-on-child-error", null, "A watched child's non-zero exit makes demux exit 2")
            .Section("Routes",
                "--to PATTERN FILE     Route lines matching regex PATTERN to FILE (repeatable).\n" +
                "--exec PATTERN CMD    Route matching lines to a command's stdin (shell-spawned, repeatable).\n" +
                "--default-to FILE     Unmatched records -> FILE.\n" +
                "--default-exec CMD    Unmatched records -> a command. (Omit both -> unmatched -> stdout.)\n" +
                "PATTERN is a bare .NET regex (not slash-delimited). Quote it to protect the shell.")
            .ExitCodes(
                (0, "Success — all input routed and delivered"),
                (1, "Partial delivery failure — a route died, records undelivered"),
                (2, "Watched child exited non-zero (--exit-on-child-error)"),
                (125, "Usage error"),
                (126, "Setup failure — could not open a --to file or launch the shell"))
            .Example(
                "cat app.log | demux --to ERROR err.log --default-exec 'gzip > rest.gz'",
                "Split errors into a file, compress the rest")
            .ComposesWith(
                "clip",
                "demux --exec ERROR clip --default-to rest.log",
                "Copy error lines to the clipboard, file the rest")
            .JsonField("tool", "string", "Tool name (\"demux\")")
            .JsonField("version", "string", "demux version")
            .JsonField("exit_code", "int", "0/1/2 — see exit codes")
            .JsonField("exit_reason", "string", "success | partial_delivery_failure | watched_child_failed")
            .JsonField("routes", "array", "Per-route {label, delivered, undelivered, dead, child_exit_code, killed_after_timeout}");
    }

    // -----------------------------------------------------------------------------------------
    // Internal helpers
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// A raw (pre-compiled) route extracted by the custom pre-pass.
    /// Pattern is "" for the default route.
    /// </summary>
    private readonly struct RawRoute
    {
        public RawRoute(TargetKind kind, string pattern, string target)
        {
            Kind = kind;
            Pattern = pattern;
            Target = target;
        }

        /// <summary>File or Exec.</summary>
        public TargetKind Kind { get; }

        /// <summary>Regex pattern text. Empty string for the default route.</summary>
        public string Pattern { get; }

        /// <summary>File path (File) or command string (Exec).</summary>
        public string Target { get; }
    }

    /// <summary>
    /// Peels route flags out of <paramref name="args"/>, populating <paramref name="routes"/>,
    /// <paramref name="def"/>, and <paramref name="residual"/>. The residual contains every
    /// argument that was not consumed as a route flag or its operands.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> on success; <see langword="false"/> with <paramref name="error"/>
    /// set on a usage violation (missing operand, duplicate default).
    /// </returns>
    private static bool ScanRoutes(
        string[] args,
        out List<RawRoute> routes,
        out RawRoute? def,
        out List<string> residual,
        out string? error)
    {
        routes = new List<RawRoute>();
        residual = new List<string>();
        def = null;
        error = null;

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            switch (a)
            {
                case "--to":
                case "--exec":
                {
                    TargetKind kind = a == "--to" ? TargetKind.File : TargetKind.Exec;
                    if (i + 2 >= args.Length)
                    {
                        // Need two more args: PATTERN and FILE/CMD.
                        string valueLabel = kind == TargetKind.File ? "FILE" : "CMD";
                        error = $"{a} requires two operands: PATTERN and {valueLabel}";
                        return false;
                    }

                    routes.Add(new RawRoute(kind, args[i + 1], args[i + 2]));
                    i += 2;
                    break;
                }

                case "--default-to":
                case "--default-exec":
                {
                    if (def is not null)
                    {
                        error = "at most one --default-to/--default-exec may be given";
                        return false;
                    }

                    TargetKind kind = a == "--default-to" ? TargetKind.File : TargetKind.Exec;
                    if (i + 1 >= args.Length)
                    {
                        error = $"{a} requires an operand";
                        return false;
                    }

                    def = new RawRoute(kind, "", args[i + 1]);
                    i += 1;
                    break;
                }

                default:
                    residual.Add(a);
                    break;
            }
        }

        return true;
    }

    /// <summary>
    /// Compiles <paramref name="pattern"/> via <see cref="SafeRegex"/> (ReDoS-safe).
    /// Writes a diagnostic to <paramref name="stderr"/> and returns <see langword="false"/>
    /// on any compile failure.
    /// </summary>
    private static bool TryCompile(string pattern, TextWriter stderr, out Regex? rx)
    {
        try
        {
            rx = SafeRegex.Create(pattern, RegexOptions.None);
            return true;
        }
        catch (RegexParseException ex)
        {
            // Don't surface ex.Message: with UseSystemResourceKeys (the AOT/trim default — see
            // demux.csproj) the framework returns an SR resource key ("MakeException, …") instead of
            // English. RegexParseException.Error/.Offset are structured and invariant-stable, so we
            // build a readable diagnostic from them.
            stderr.WriteLine($"demux: invalid regex '{pattern}': {ex.Error} at offset {ex.Offset}");
            rx = null;
            return false;
        }
        catch (Exception)
        {
            // Any other compile failure: same rule — never echo a framework ex.Message to the user.
            stderr.WriteLine($"demux: invalid regex '{pattern}': not a valid regular expression");
            rx = null;
            return false;
        }
    }
}
