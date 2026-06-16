#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Winix.ProcessSupervision;
using Yort.ShellKit;

namespace Winix.RunFor;

/// <summary>
/// Library entry point for runfor: parse → build options → run the deadline orchestrator → format →
/// exit. <c>Program.Main</c> is a thin shell owning Ctrl+C. runfor's own output (the stderr notice
/// and the --json envelope) goes to <paramref name="stderr"/>; the child inherits the real
/// stdout/stderr, so runfor never writes to the child's stdout.
/// </summary>
public static class Cli
{
    /// <summary>Runs the runfor CLI.</summary>
    /// <param name="args">Raw command-line args: <c>[options] DURATION -- command [args...]</c>.</param>
    /// <param name="stdout">Used only for parser introspection (--help/--describe/--version write to
    /// Console.Out directly; this writer is present for API symmetry with other Winix Cli.Run seams).</param>
    /// <param name="stderr">runfor's notice + --json envelope + all errors.</param>
    /// <param name="cancellationToken">Ctrl+C (owned by Program.Main).</param>
    /// <param name="starter">Child starter; defaults to the real process starter. Injected in tests.</param>
    /// <returns>Forwarded child code, 124 (deadline), 130 (Ctrl+C), or 125/126/127 (runfor errors).</returns>
    public static int Run(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken cancellationToken,
        IChildStarter? starter = null)
    {
        string version = GetVersion();

        var parser = new CommandLineParser("runfor", version)
            .Description("Run a command with a time limit — cross-platform timeout(1).")
            .Maturity(ToolMaturity.Fresh)
            .StandardFlags()
            .Positional("DURATION")
            .Option("--signal", "-s", "NAME", "Signal sent at the deadline on Unix: TERM (default), HUP, INT, QUIT, KILL. Ignored on Windows.")
            .Option("--kill-after", "-k", "GRACE", "Unix: after the deadline signal, wait GRACE then SIGKILL the tree. No-op on Windows (kills immediately).")
            .PreferDefaultWhen(
                "You only need to sleep for a fixed duration (use timeout.exe or sleep instead — runfor launches a command)",
                "You are on Unix and the command respects SIGTERM reliably and you have no need for --json or a consistent exit-code family")
            .ExitCodes(
                (0, "Child exited 0 before the deadline (or forwarded child code 1–123)"),
                (SupervisionExitCode.Timeout, "Deadline exceeded — the child was terminated"),
                (SupervisionExitCode.Interrupted, "Interrupted by Ctrl+C"),
                (ExitCode.UsageError, "Usage error: missing/invalid DURATION, no command, bad --signal/--kill-after"),
                (ExitCode.NotExecutable, "Command not executable"),
                (ExitCode.NotFound, "Command not found on PATH"))
            .Platform(
                "cross-platform",
                new[] { "timeout" },
                "Windows timeout.exe only SLEEPS — it does not bound a command; runfor actually enforces a deadline",
                "Same role as coreutils timeout, with a consistent --json envelope and exit-code family across platforms")
            .StdinDescription("Not used (child process inherits stdin)")
            .StdoutDescription("Child process stdout passes through unmodified")
            .StderrDescription("Child stderr passes through; runfor's own notice and --json summary also go here")
            .Example("runfor 30s -- curl https://example.com", "Abort a request after 30 seconds")
            .Example("runfor 5m -- dotnet test", "Cap a test run at 5 minutes")
            .Example("runfor --kill-after 3s 10s -- ./server", "SIGTERM at 10s, SIGKILL 3s later if it ignores it (Unix)")
            .Example("runfor --signal INT 1m -- ./job", "Send SIGINT instead of SIGTERM at the deadline (Unix)")
            .JsonField("tool", "string", "Tool name (\"runfor\")")
            .JsonField("version", "string", "Tool version")
            .JsonField("exit_code", "int", "runfor's exit code (child's on completion, 124 timeout, 130 interrupt, 126/127 launch)")
            .JsonField("outcome", "string", "completed | timed_out | interrupted | launch_failed")
            .JsonField("timed_out", "bool", "True iff the deadline fired")
            .JsonField("child_exit_code", "int|null", "Child's exit code if it completed, else null")
            .JsonField("signal", "string", "Configured deadline signal name; emitted on all platforms (Windows sends no signal)")
            .JsonField("kill_failed", "bool", "True iff a kill was attempted and could not be confirmed")
            .JsonField("duration_ms", "int", "Wall-clock time from launch to resolution, milliseconds");

        ParseResult result = parser.Parse(args);
        if (result.IsHandled) { return result.ExitCode; }
        if (result.HasErrors) { return result.WriteErrors(stderr); }

        string[] positionals = result.Positionals;
        if (positionals.Length == 0)
        {
            return result.WriteError("no DURATION given. Usage: runfor DURATION -- command [args...]", stderr);
        }

        if (!DurationParser.TryParse(positionals[0], out TimeSpan deadline) || deadline <= TimeSpan.Zero)
        {
            return result.WriteError($"invalid duration: '{positionals[0]}' (e.g. 500ms, 30s, 5m, 1h)", stderr);
        }

        // positionals[0] is DURATION; everything after is the child command + args. The '--' boundary
        // (non-CommandMode) routes post-'--' tokens into Positionals without flag-parsing, so child
        // flags survive. Use '--' before commands that take their own dashed flags.
        string[] childArgv = positionals.Skip(1).ToArray();
        if (childArgv.Length == 0)
        {
            return result.WriteError("no command given. Usage: runfor DURATION -- command [args...]", stderr);
        }

        int signal = UnixSignal.DefaultSignal;
        if (result.Has("--signal"))
        {
            string sigStr = result.GetString("--signal");
            if (!UnixSignal.TryParse(sigStr, out signal))
            {
                return result.WriteError($"invalid --signal: '{sigStr}' (one of TERM, HUP, INT, QUIT, KILL)", stderr);
            }
        }

        TimeSpan? killAfter = null;
        if (result.Has("--kill-after"))
        {
            string kaStr = result.GetString("--kill-after");
            // DurationParser already rejects a leading sign, so `ka < Zero` is belt-and-braces — kept
            // so the contract (no negative grace) is enforced at this layer regardless of parser changes.
            if (!DurationParser.TryParse(kaStr, out TimeSpan ka) || ka < TimeSpan.Zero)
            {
                return result.WriteError($"invalid --kill-after: '{kaStr}' (e.g. 2s, 500ms)", stderr);
            }
            killAfter = ka;
        }

        bool jsonOutput = result.Has("--json");
        // useColor applies to the stderr notice (runfor's own output, not the child's streams).
        bool useColor = result.ResolveColor(checkStdErr: true);

        var options = new RunForOptions(deadline, signal, killAfter);
        string command = childArgv[0];
        string[] commandArgs = childArgv.Skip(1).ToArray();

        IChildStarter effectiveStarter = starter ?? new ProcessChildStarter();

        RunForResult runResult;
        try
        {
            runResult = RunForRunner.Execute(effectiveStarter, command, commandArgs, options, cancellationToken);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            string msg = string.IsNullOrEmpty(ex.Message)
                ? $"runfor: unexpected error: {ex.GetType().Name}"
                : $"runfor: unexpected error: {ex.GetType().Name}: {ex.Message}";
            SafeWriteLine(stderr, msg);
            return ExitCode.NotExecutable;
        }

        // All of runfor's own output goes to STDERR (the child owns stdout). One JSON path for every
        // outcome; otherwise LaunchFailed prints a classified reason and the timeout/interrupt notice
        // is emitted only when non-empty (clean completion is silent on success).
        if (jsonOutput)
        {
            SafeWriteLine(stderr, Formatting.FormatJson(runResult, "runfor", version, UnixSignal.ToName(signal)));
        }
        else if (runResult.Outcome == RunForOutcome.LaunchFailed)
        {
            SafeWriteLine(stderr, $"runfor: {command}: {ExitReasonText(runResult.ExitCode)}");
        }
        else
        {
            string notice = Formatting.FormatNotice(runResult, command, deadline, useColor);
            if (!string.IsNullOrEmpty(notice)) { SafeWriteLine(stderr, notice); }
        }

        return runResult.ExitCode;
    }

    private static string ExitReasonText(int exitCode)
    {
        if (exitCode == ExitCode.NotFound) { return "command not found"; }
        if (exitCode == ExitCode.NotExecutable) { return "command not executable"; }
        return "launch failed";
    }

    private static void SafeWriteLine(TextWriter writer, string message)
    {
        try { writer.WriteLine(message); }
        catch (IOException) { /* downstream pipe closed */ }
        catch (ObjectDisposedException) { /* writer disposed */ }
    }

    private static string GetVersion()
    {
        string raw = typeof(RunForResult).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
        int plus = raw.IndexOf('+');
        return plus >= 0 ? raw.Substring(0, plus) : raw;
    }
}
