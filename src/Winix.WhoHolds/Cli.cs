#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Yort.ShellKit;

namespace Winix.WhoHolds;

/// <summary>
/// Library-level entry point for the whoholds CLI. <c>Program.cs</c> is a thin shim
/// around <see cref="Run"/> that wires up <c>Console.*</c> and forwards the exit code;
/// all orchestration lives here so it can be exercised by unit tests.
/// </summary>
/// <remarks>
/// Round-1 tier-2 review extraction (test-analyzer C1-C4). The seam exposes:
/// <list type="bullet">
///   <item>The <see cref="FindResult.QueryFailed"/> → exit 1 routing (SFH I1+I2+I3 fix).</item>
///   <item>The elevation-warning emission branch.</item>
///   <item>The <c>--json</c> output streaming (success and error envelopes both to stdout per
///         suite convention, matching the man-F12 fix from 2026-05-08).</item>
///   <item>The target-not-found exit-1 path (tier-2 baseline finding F2).</item>
/// </list>
/// to deterministic testing without spawning a process.
/// </remarks>
public static class Cli
{
    /// <summary>
    /// Runs the whoholds pipeline: parse args, dispatch to file or port finder, emit output,
    /// return exit code. All side effects are routed through the supplied parameters — no
    /// references to <c>Console.*</c> inside this method.
    /// </summary>
    /// <param name="args">Command-line arguments (without the executable name).</param>
    /// <param name="stdout">Output writer for results (table, PID-only, or JSON envelope).</param>
    /// <param name="stderr">Error writer for the elevation warning, no-results message, and plain-text errors.</param>
    /// <param name="isStdoutRedirected">
    /// Whether stdout is redirected (<c>Console.IsOutputRedirected</c> in production). Drives
    /// the auto-<c>--pid-only</c> behaviour. Tests pass an explicit value to exercise both branches.
    /// </param>
    /// <param name="fileFinder">
    /// Optional seam for file lock queries; defaults to platform dispatch
    /// (Restart Manager on Windows, lsof off-Windows when available, otherwise
    /// <see cref="FindResult.Empty"/>). Tests inject fakes to exercise the
    /// <see cref="FindResult.QueryFailed"/> routing without spawning processes.
    /// </param>
    /// <param name="portFinder">
    /// Optional seam for port lock queries; same defaults as <paramref name="fileFinder"/>.
    /// </param>
    /// <param name="isElevated">
    /// Optional seam for elevation detection; defaults to
    /// <see cref="ElevationDetector.IsElevated"/>. Tests inject a fake to pin the
    /// elevation-warning branches.
    /// </param>
    /// <returns>Process exit code: 0 success (includes no holders), 1 target not found or
    /// backend query error, 125 usage error.</returns>
    public static int Run(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        bool isStdoutRedirected,
        Func<string, FindResult>? fileFinder = null,
        Func<int, FindResult>? portFinder = null,
        Func<bool>? isElevated = null)
    {
        fileFinder ??= DefaultFileFinder;
        portFinder ??= DefaultPortFinder;
        isElevated ??= ElevationDetector.IsElevated;

        string version = GetVersion();
        var parser = ConfigureParser(version);
        var result = parser.Parse(args);
        if (result.IsHandled) { return result.ExitCode; }
        if (result.HasErrors) { return result.WriteErrors(stderr); }

        string[] positionals = result.Positionals;
        if (positionals.Length != 1)
        {
            stderr.WriteLine("whoholds: expected exactly one argument: <file-or-port>");
            return ExitCode.UsageError;
        }

        ParsedArgument parsed = ArgumentParser.Parse(positionals[0]);
        if (parsed.IsError)
        {
            stderr.WriteLine($"whoholds: {parsed.ErrorMessage}");
            return ExitCode.UsageError;
        }

        bool jsonOutput = result.Has("--json");
        // The elevation warning goes to stderr, so check stderr for colour support.
        bool useColor = result.ResolveColor(checkStdErr: true);
        bool pidOnly = result.Has("--pid-only") || isStdoutRedirected;
        bool showFullPath = result.Has("--full-path");

        if (!isElevated())
        {
            stderr.WriteLine(Formatting.FormatElevationWarning(useColor));
        }

        FindResult findResult;
        string resource;

        if (parsed.IsFile)
        {
            resource = parsed.FilePath!;

            // Tier-2 baseline 2026-05-06 finding F2 — see ArgumentParser remarks. The parser
            // accepts a path-with-separator unconditionally; this method is responsible for
            // catching the missing-target case and routing it to exit 1 per the README contract.
            if (!File.Exists(resource) && !Directory.Exists(resource))
            {
                EmitErrorEnvelope(jsonOutput, stdout, stderr, version,
                    plainText: $"whoholds: target not found: '{resource}'",
                    exitReason: "target_not_found",
                    detail: $"target not found: '{resource}'");
                return 1;
            }

            findResult = fileFinder(resource);
        }
        else
        {
            resource = $":{parsed.Port}";
            findResult = portFinder(parsed.Port);
        }

        // SFH I1+I2+I3 (round-1 review 2026-05-08): a backend API failure surfaces as
        // QueryFailed=true with a human-readable Reason. SFH F1 (round-1 fresh-eyes
        // 2026-05-08): when --json is passed, route the failure into a JSON error envelope
        // to stdout instead of plain text on stderr; otherwise structured-output consumers
        // see no JSON at all on the documented exit-1 path.
        if (findResult.QueryFailed)
        {
            string reason = findResult.Reason ?? "backend query failed";
            EmitErrorEnvelope(jsonOutput, stdout, stderr, version,
                plainText: $"whoholds: query failed for '{resource}': {reason}",
                exitReason: "query_failed",
                detail: reason);
            return 1;
        }

        IReadOnlyList<LockInfo> locks = findResult.Results;

        // SFH F2 + Docs-auditor I1 + CR I1 (fresh-eyes 2026-05-08): JSON output goes to
        // stdout per the suite convention established in 2026-05-08 (man-F12, winix-F3).
        // Pre-fix, --json went to stderr alongside the elevation warning; pipelines like
        // `whoholds :8080 --json | jq '.processes[].name'` produced no JSON on stdout.
        if (jsonOutput)
        {
            string json = Formatting.FormatJson(locks, ExitCode.Success, "success", "whoholds", version);
            stdout.WriteLine(json);
            return ExitCode.Success;
        }

        if (locks.Count == 0)
        {
            stderr.WriteLine(Formatting.FormatNoResults(resource));
            return ExitCode.Success;
        }

        if (pidOnly)
        {
            stdout.Write(Formatting.FormatPidOnly(locks));
        }
        else
        {
            stdout.Write(Formatting.FormatTable(locks, useColor, showFullPath));
        }

        return ExitCode.Success;
    }

    /// <summary>
    /// Emits a structured error envelope to stdout (<c>--json</c>) or a plain-text error to
    /// stderr (default), as required by the <paramref name="jsonOutput"/> mode.
    /// </summary>
    private static void EmitErrorEnvelope(
        bool jsonOutput,
        TextWriter stdout,
        TextWriter stderr,
        string version,
        string plainText,
        string exitReason,
        string detail)
    {
        if (jsonOutput)
        {
            string json = Formatting.FormatJsonError(1, exitReason, "whoholds", version, detail);
            stdout.WriteLine(json);
        }
        else
        {
            stderr.WriteLine(plainText);
        }
    }

    /// <summary>
    /// Builds the ShellKit <see cref="CommandLineParser"/> for whoholds. Extracted so the
    /// CLI shape is configured in one place; <see cref="Program"/> and tests share it.
    /// </summary>
    internal static CommandLineParser ConfigureParser(string version)
    {
        return new CommandLineParser("whoholds", version)
            .Description("Find which processes are holding a file lock or binding a network port.")
            .Maturity(ToolMaturity.Core)
            .Flag("--pid-only", "Force one-PID-per-line output (auto when piped)")
            .Flag("--full-path", "-l", "Show full executable path instead of process name")
            .StandardFlags()
            .Positional("<file-or-port>")
            .Platform("cross-platform",
                new[] { "handle.exe", "lsof" },
                "Windows has no built-in CLI for file/port locks",
                "Unified syntax for both files and ports; lsof delegation with clean output")
            .StdinDescription("Not used")
            .StdoutDescription("PID-only or table of locking processes (PID-only when --pid-only or piped); --json envelope.")
            .StderrDescription("Elevation warning, no-results message, and plain-text errors.")
            .Example("whoholds myfile.dll", "Find what's locking a file")
            .Example("whoholds :8080", "Find what's binding port 8080")
            .Example("whoholds myfile.dll --pid-only | wargs taskkill /PID {} /F", "Kill all processes locking a file")
            .ComposesWith("wargs", "whoholds myfile.dll --pid-only | wargs taskkill /PID {} /F", "Kill all processes locking a file")
            .JsonField("tool", "string", "Tool name (\"whoholds\")")
            .JsonField("version", "string", "Tool version")
            .JsonField("exit_code", "int", "Tool exit code (0 = success)")
            .JsonField("exit_reason", "string", "Machine-readable exit reason")
            .JsonField("processes", "array", "Array of locking process objects")
            .JsonField("processes[].pid", "int", "Process ID")
            .JsonField("processes[].name", "string", "Process name")
            .JsonField("processes[].path", "string", "Full executable path (empty if unavailable)")
            .JsonField("processes[].state", "string", "TCP state (e.g. LISTEN); empty for files and UDP")
            .JsonField("processes[].resource", "string", "Locked file path or port specifier")
            .ExitCodes(
                (ExitCode.Success, "Success (includes no-results)"),
                (1, "Target not found or query error (file/directory does not exist, or backend API failure)"),
                (ExitCode.UsageError, "Usage error (bad flags, missing positional, invalid port number, or unrecognised argument shape)"));
    }

    /// <summary>
    /// Production file-lock finder. Dispatches to the platform-appropriate backend; when
    /// no backend is available (e.g. Linux without lsof) returns success-empty rather than
    /// QueryFailed — see <see cref="FindResult"/> remarks for the design rationale.
    /// </summary>
    private static FindResult DefaultFileFinder(string filePath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return FileLockFinder.Find(filePath);
        }
        if (LsofFinder.IsAvailable())
        {
            return LsofFinder.FindFile(filePath);
        }
        return FindResult.Empty;
    }

    private static FindResult DefaultPortFinder(int port)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return PortLockFinder.Find(port);
        }
        if (LsofFinder.IsAvailable())
        {
            return LsofFinder.FindPort(port);
        }
        return FindResult.Empty;
    }

    /// <summary>
    /// Returns the informational version from the Winix.WhoHolds library assembly.
    /// </summary>
    private static string GetVersion()
    {
        // SDK appends a SourceLink "+gitsha" suffix to AssemblyInformationalVersion
        // by default; strip it so users see plain "X.Y.Z" — matches the convention
        // adopted across clip / digest / ids / schedule / etc.
        string raw = typeof(LockInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
        int plus = raw.IndexOf('+');
        return plus >= 0 ? raw.Substring(0, plus) : raw;
    }
}
