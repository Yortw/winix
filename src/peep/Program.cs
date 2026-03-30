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
                (ExitCode.NotFound, "Command not found"));

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
                .Select(p => new Regex(p, RegexOptions.Compiled))
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
                    exitCode: peepResult.ExitCode == 0 ? 0 : peepResult.ExitCode,
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
                Console.Error.WriteLine(Formatting.FormatJsonError(127, "command_not_found", "peep", version));
            }
            else
            {
                Console.Error.WriteLine($"peep: {ex.Message}");
            }
            return 127;
        }
        catch (CommandNotExecutableException ex)
        {
            if (jsonOutput)
            {
                Console.Error.WriteLine(Formatting.FormatJsonError(126, "command_not_executable", "peep", version));
            }
            else
            {
                Console.Error.WriteLine($"peep: {ex.Message}");
            }
            return 126;
        }
    }

    private static string GetVersion()
    {
        return typeof(PeepResult).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
    }
}
