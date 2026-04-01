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
        if (result.HasErrors) return result.WriteErrors(Console.Error);

        double intervalSeconds = result.GetDouble("--interval", defaultValue: 2.0);
        bool intervalExplicit = result.Has("--interval");
        string[] watchPatterns = result.GetList("--watch");
        bool once = result.Has("--once");
        bool jsonOutput = result.Has("--json") || result.Has("--json-output");

        if (result.Command.Length == 0)
        {
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

        try
        {
            PeepResult peepResult = await CommandExecutor.RunAsync(command, commandArgs, TriggerSource.Initial);
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
                    version: version));
            }

            return peepResult.ExitCode;
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
    }

    private static string GetVersion()
    {
        return typeof(PeepResult).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
    }

}
