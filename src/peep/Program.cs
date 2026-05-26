using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using Winix.Peep;
using Yort.ShellKit;

namespace Peep;

internal sealed class Program
{
    static async Task<int> Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();
        string version = GetVersion();

        var parser = new CommandLineParser("peep", version)
            .Description("Run a command repeatedly and display output on a refreshing screen.")
            .StandardFlags()
            .DoubleOption("--interval", "-n", "N", "Seconds between runs (default: 2)",
                validate: v => v > 0 ? null : "must be positive")
            .ListOption("--watch", "-w", "GLOB", "Re-run on file changes matching glob")
            .IntOption("--debounce", null, "N", "Milliseconds to debounce file changes (default: 300)",
                validate: v => v >= 0 ? null : "must be non-negative")
            .IntOption("--history", null, "N", "Max history snapshots to retain (default: 1000, 0=unlimited)",
                validate: v => v >= 0 ? null : "must be non-negative")
            .Flag("--exit-on-change", "-g", "Exit when output changes")
            .Flag("--exit-on-success", "Exit when command returns exit code 0")
            .Flag("--exit-on-error", "-e", "Exit when command returns non-zero")
            .ListOption("--exit-on-match", null, "PAT", "Exit when output matches regex")
            .Flag("--differences", "-d", "Highlight changed lines between runs")
            .Flag("--no-gitignore", "Disable automatic .gitignore filtering")
            .Flag("--once", "Run once, display, and exit")
            .Flag("--no-header", "-t", "Hide the header lines")
            .Flag("--json-output", "Include last captured output in JSON (implies --json)")
            .Section("Compatibility",
                """
                These flags match watch for muscle memory:
                -n N                   Same as --interval
                -g                     Same as --exit-on-change
                -e                     Same as --exit-on-error
                -d                     Same as --differences
                -t                     Same as --no-header
                """)
            .Section("Interactive",
                """
                q / Ctrl+C             Quit
                Space                  Pause/unpause display
                r / Enter              Force immediate re-run
                d                      Toggle diff highlighting
                Up/Down / PgUp/Dn     Scroll while paused
                Left/Right             Time travel (older/newer)
                t                      History overlay
                ?                      Show/hide help overlay
                """)
            .CommandMode()
            .ExitCodes(
                (0, "Auto-exit condition met, or manual quit with last child exit 0"),
                (ExitCode.UsageError, "Usage error"),
                (ExitCode.NotExecutable, "Command not executable"),
                (ExitCode.NotFound, "Command not found"))
            .Platform("cross-platform",
                replaces: new[] { "watch", "entr" },
                valueOnWindows: "No native watch or entr equivalent on Windows",
                valueOnUnix: "Combines watch + entr in one tool with diff highlighting")
            .StdinDescription("Not used")
            .StdoutDescription("Child process output displayed on refreshing screen")
            .StderrDescription("Errors and diagnostics")
            .Example("peep -- git status", "Watch git status every 2 seconds")
            .Example("peep -n 5 -- kubectl get pods", "Watch pods every 5 seconds")
            .Example("peep -w 'src/**/*.cs' -- dotnet test", "Re-run tests on file change")
            .Example("peep -w 'src/**/*.cs' -e 0 -- dotnet test", "Run tests on file change, exit on first success")
            .ComposesWith("files", "peep -- files . --newer 5m --type f", "Watch for recently created files")
            .ComposesWith("timeit", "peep -- timeit dotnet build", "Monitor build times")
            .JsonField("tool", "string", "Tool name (\"peep\")")
            .JsonField("version", "string", "Tool version")
            .JsonField("exit_code", "int", "Tool exit code (0 = success)")
            .JsonField("exit_reason", "string", "Machine-readable exit reason (manual, once, exit_on_success, etc.)")
            .JsonField("runs", "int", "Total command executions during session")
            .JsonField("last_child_exit_code", "int|null", "Exit code of the final child run")
            .JsonField("duration_seconds", "float", "Total session wall time in seconds")
            .JsonField("command", "string", "The watched command")
            .JsonField("last_output", "string|null", "Captured output from final run (with --json-output)")
            .JsonField("history_retained", "int|null", "Snapshots retained at session end");

        var result = parser.Parse(args);
        if (result.IsHandled) return result.ExitCode;

        // R5 SFH I1: peep treats --json-output as JSON-implying for envelope output
        // (line below: jsonOutput = --json || --json-output), but ShellKit's
        // WriteError / WriteErrors only honour --json. Without bridging here,
        //   peep --json-output             (no command)            → plain-text error
        //   peep --json-output --exit-on-match '['                 → plain-text error
        // breaks JSON-aware automation that decides "envelope or not" by inspecting
        // --json-output. When the user has --json-output without --json, emit our
        // own JSON envelope on the early-return paths.
        bool jsonOnlyViaJsonOutput = !result.Has("--json") && result.Has("--json-output");

        if (result.HasErrors)
        {
            if (jsonOnlyViaJsonOutput)
            {
                Console.Error.WriteLine(Formatting.FormatJsonError(
                    ExitCode.UsageError, "usage_error", "peep", version));
                return ExitCode.UsageError;
            }
            return result.WriteErrors(Console.Error);
        }

        double intervalSeconds = result.GetDouble("--interval", defaultValue: 2.0);
        bool intervalExplicit = result.Has("--interval");
        string[] watchPatterns = result.GetList("--watch");
        bool once = result.Has("--once");
        bool jsonOutput = result.Has("--json") || result.Has("--json-output");

        if (result.Command.Length == 0)
        {
            if (jsonOnlyViaJsonOutput)
            {
                Console.Error.WriteLine(Formatting.FormatJsonError(
                    ExitCode.UsageError, "usage_error", "peep", version));
                return ExitCode.UsageError;
            }
            return result.WriteError("no command specified. Run 'peep --help' for usage.", Console.Error);
        }

        string command = result.Command[0];
        string[] commandArgs = result.Command.Skip(1).ToArray();
        string commandDisplay = string.Join(" ", result.Command);

        Regex[] exitOnMatchRegexes;
        try
        {
            exitOnMatchRegexes = result.GetList("--exit-on-match")
                .Select(p => SafeRegex.Create(p, RegexOptions.None))
                .ToArray();
        }
        catch (RegexParseException ex)
        {
            if (jsonOnlyViaJsonOutput)
            {
                Console.Error.WriteLine(Formatting.FormatJsonError(
                    ExitCode.UsageError, "usage_error", "peep", version));
                return ExitCode.UsageError;
            }
            return result.WriteError($"invalid regex pattern: {ex.Message}", Console.Error);
        }

        if (once)
        {
            return await RunOnceAsync(command, commandArgs, commandDisplay,
                jsonOutput, result.Has("--json-output"), version);
        }

        var config = new SessionConfig(
            Command: command,
            CommandArgs: commandArgs,
            CommandDisplay: commandDisplay,
            IntervalSeconds: intervalSeconds,
            UseInterval: watchPatterns.Length == 0 || intervalExplicit,
            WatchPatterns: watchPatterns,
            DebounceMs: result.GetInt("--debounce", defaultValue: 300),
            HistoryCapacity: result.GetInt("--history", defaultValue: 1000),
            NoGitIgnore: result.Has("--no-gitignore"),
            ExitOnChange: result.Has("--exit-on-change"),
            ExitOnSuccess: result.Has("--exit-on-success"),
            ExitOnError: result.Has("--exit-on-error"),
            ExitOnMatchRegexes: exitOnMatchRegexes,
            DiffEnabled: result.Has("--differences"),
            NoHeader: result.Has("--no-header"),
            JsonOutput: jsonOutput,
            JsonOutputIncludeOutput: result.Has("--json-output"),
            UseColor: result.ResolveColor(),
            Version: version);

        var session = new InteractiveSession(config);
        return await session.RunAsync(CancellationToken.None);
    }

    private static async Task<int> RunOnceAsync(
        string command, string[] commandArgs, string commandDisplay,
        bool jsonOutput, bool jsonOutputIncludeOutput, string version)
    {
        var sessionStopwatch = Stopwatch.StartNew();

        // CR I4 / CR I9: --once must respect Ctrl+C and not orphan the child.
        // Without our own CTS + CancelKeyPress handler, hitting Ctrl+C during
        // `peep --once -- some-slow-cmd` lets the .NET default handler tear down
        // peep without ever cancelling the token we passed to CommandExecutor —
        // so its kill-on-cancel callback never fires and the child leaks.
        using var cts = new CancellationTokenSource();
        // R4 TA I6: handler body lives in SessionHelpers.RequestCancellationSilently
        // so the "Cancel after dispose must not throw" contract is regression-pinned
        // (Console.CancelKeyPress is a static event that doesn't compose with xunit).
        ConsoleCancelEventHandler cancelHandler = (_, e)
            => SessionHelpers.RequestCancellationSilently(e, cts);
        Console.CancelKeyPress += cancelHandler;

        // R6 SFH N1: hoist peepResult to the outer scope so the last-resort catch
        // arm can recover the child's exit code if FormatJson or a Console.Error
        // .WriteLine throws after the child has successfully run. Without the hoist,
        // an automation script invoking `peep --once --json -- somecmd` would see
        // exit 126 (catch-all) instead of the child's actual exit code in that
        // narrow window. Trigger has no deterministic reproducer on .NET 10 — flag
        // is hypothesis-class — but the defensive hoist is one line and removes the
        // exit-code-loss failure mode entirely.
        PeepResult? peepResult = null;
        try
        {
            peepResult = await CommandExecutor.RunAsync(
                command, commandArgs, TriggerSource.Initial, cts.Token);
            sessionStopwatch.Stop();

            Console.Write(peepResult.Output);

            if (jsonOutput)
            {
                Console.Error.WriteLine(Formatting.FormatJson(
                    exitCode: peepResult.ExitCode,
                    exitReason: "once",
                    runs: 1,
                    lastChildExitCode: peepResult.ExitCode,
                    durationSeconds: sessionStopwatch.Elapsed.TotalSeconds,
                    command: commandDisplay,
                    lastOutput: jsonOutputIncludeOutput ? peepResult.Output : null,
                    toolName: "peep",
                    version: version,
                    // TA R2-C4: --describe advertises history_retained, so emit it as 0
                    // for once-mode rather than omitting (would fail describe-vs-actual
                    // contract checks). Once-mode keeps zero snapshots.
                    historyRetained: 0));
            }

            return peepResult.ExitCode;
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C during once-mode. CommandExecutor's cancellation-Register kill
            // already terminated the child process tree. Exit 130 (POSIX cancelled).
            sessionStopwatch.Stop();
            if (jsonOutput)
            {
                Console.Error.WriteLine(Formatting.FormatJsonError(130, "cancelled", "peep", version));
            }
            return 130;
        }
        catch (CommandNotFoundException ex)
        {
            if (jsonOutput)
            {
                Console.Error.WriteLine(Formatting.FormatJsonError(ExitCode.NotFound, "command_not_found", "peep", version));
            }
            else
            {
                Console.Error.WriteLine($"peep: {ex.Message}");
            }
            return ExitCode.NotFound;
        }
        catch (CommandNotExecutableException ex)
        {
            if (jsonOutput)
            {
                Console.Error.WriteLine(Formatting.FormatJsonError(ExitCode.NotExecutable, "command_not_executable", "peep", version));
            }
            else
            {
                Console.Error.WriteLine($"peep: {ex.Message}");
            }
            return ExitCode.NotExecutable;
        }
        catch (CommandStreamException ex)
        {
            // CR I9: child closed a pipe abnormally. CommandExecutor has already killed
            // the child. The interactive path's TryRunCommandAsync handles this; once-mode
            // previously did not, so a stream error escaped as an unhandled exception with
            // no envelope and an unspecified exit. Mirror the typed-exception arm above.
            if (jsonOutput)
            {
                Console.Error.WriteLine(Formatting.FormatJsonError(ExitCode.NotExecutable, "command_stream_failed", "peep", version));
            }
            else
            {
                Console.Error.WriteLine($"peep: {ex.Message}");
            }
            return ExitCode.NotExecutable;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // R5 SFH I2: symmetry with InteractiveSession.TryRunCommandAsync's last-
            // resort catch. Any exception escaping CommandExecutor.RunAsync that
            // isn't one of the typed arms above (e.g. an InvalidOperationException
            // from a process-handle race during teardown, or a CTS-Register failure
            // at registration time) would otherwise propagate out of Main as an
            // unhandled exception with no JSON envelope under --json — breaking
            // automation that depends on every exit path emitting an envelope. The
            // interactive path was given this safety net in round 2; once-mode
            // lacked it until now. OOM/SO are deliberately not caught (they should
            // crash the process per the project convention).
            //
            // R6 SFH N1: prefer the child's actual exit code if RunAsync succeeded
            // (i.e. peepResult is non-null) and the exception fired downstream
            // (FormatJson / Console.Error.WriteLine). Falls back to NotExecutable
            // (126) only when RunAsync itself threw the unexpected exception.
            int exitCode = peepResult?.ExitCode ?? ExitCode.NotExecutable;
            if (jsonOutput)
            {
                Console.Error.WriteLine(Formatting.FormatJsonError(
                    exitCode, "command_unexpected_error", "peep", version));
            }
            else
            {
                Console.Error.WriteLine($"peep: unexpected error running command: {ex.GetType().Name}: {ex.Message}");
            }
            return exitCode;
        }
        finally
        {
            // Unregister BEFORE the using disposes the CTS, so a late Ctrl+C cannot
            // call Cancel() on a disposed source and surface as ObjectDisposedException
            // (the cancel-handler swallows that, but unregistering first is cleaner).
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static string GetVersion()
    {
        // Match the convention used by clip / ids / digest / envvault — read
        // AssemblyInformationalVersion (injected via /p:Version by the release
        // pipeline) and strip the "+gitsha" SourceLink suffix the SDK appends
        // by default. Users see "0.3.0", not "0.3.0+abc123…".
        string raw = typeof(PeepResult).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
        int plus = raw.IndexOf('+');
        return plus >= 0 ? raw[..plus] : raw;
    }

}
