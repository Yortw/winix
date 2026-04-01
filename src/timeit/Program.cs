using System.Reflection;
using Winix.TimeIt;
using Yort.ShellKit;

namespace TimeIt;

internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        string version = GetVersion();

        var parser = new CommandLineParser("timeit", version)
            .Description("Time a command and show wall clock, CPU time, peak memory, and exit code.")
            .StandardFlags()
            .Flag("--oneline", "-1", "Single-line output format")
            .Flag("--stdout", "Write summary to stdout instead of stderr")
            .CommandMode()
            .ExitCodes(
                (0, "Child process exit code (pass-through)"),
                (ExitCode.UsageError, "No command specified or bad timeit arguments"),
                (ExitCode.NotExecutable, "Command not executable (permission denied)"),
                (ExitCode.NotFound, "Command not found"))
            .Platform("cross-platform",
                replaces: new[] { "time" },
                valueOnWindows: "No native time command; Measure-Command is verbose and doesn't stream output",
                valueOnUnix: "Richer output than time — peak memory, CPU breakdown, JSON output")
            .StdinDescription("Not used (child process inherits stdin)")
            .StdoutDescription("Child process stdout passes through unmodified")
            .StderrDescription("Timing summary after child exits. JSON with --json. Child stderr also passes through.")
            .Example("timeit dotnet build", "Time a build")
            .Example("timeit --json dotnet test", "Machine-parseable timing for CI")
            .Example("timeit --stdout dotnet build 2>/dev/null", "Capture just the timing summary")
            .ComposesWith("peep", "peep -- timeit dotnet build", "Watch build time on every file change")
            .JsonField("tool", "string", "Tool name (\"timeit\")")
            .JsonField("version", "string", "Tool version")
            .JsonField("exit_code", "int", "Tool exit code (0 = success)")
            .JsonField("exit_reason", "string", "Machine-readable exit reason")
            .JsonField("child_exit_code", "int|null", "Child process exit code")
            .JsonField("wall_seconds", "float", "Wall clock time in seconds")
            .JsonField("user_cpu_seconds", "float|null", "User CPU time in seconds")
            .JsonField("sys_cpu_seconds", "float|null", "System CPU time in seconds")
            .JsonField("cpu_seconds", "float|null", "Total CPU time (user + sys)")
            .JsonField("peak_memory_bytes", "int|null", "Peak working set in bytes");

        var result = parser.Parse(args);
        if (result.IsHandled) return result.ExitCode;
        if (result.HasErrors) return result.WriteErrors(Console.Error);

        bool oneLine = result.Has("--oneline");
        bool jsonOutput = result.Has("--json");
        bool useStdout = result.Has("--stdout");
        bool useColor = result.ResolveColor();
        TextWriter writer = useStdout ? Console.Out : Console.Error;

        if (result.Command.Length == 0)
        {
            return result.WriteError("no command specified. Run 'timeit --help' for usage.", writer);
        }

        string command = result.Command[0];
        string[] commandArgs = result.Command.Skip(1).ToArray();

        TimeItResult timeResult;
        try
        {
            timeResult = CommandRunner.Run(command, commandArgs);
        }
        catch (CommandNotExecutableException ex)
        {
            if (jsonOutput)
            {
                // Errors always go to stderr, even when --stdout redirects summary output
                Console.Error.WriteLine(Formatting.FormatJsonError(ExitCode.NotExecutable, "command_not_executable", "timeit", version));
            }
            else
            {
                Console.Error.WriteLine($"timeit: {ex.Message}");
            }
            return ExitCode.NotExecutable;
        }
        catch (CommandNotFoundException ex)
        {
            if (jsonOutput)
            {
                // Errors always go to stderr, even when --stdout redirects summary output
                Console.Error.WriteLine(Formatting.FormatJsonError(ExitCode.NotFound, "command_not_found", "timeit", version));
            }
            else
            {
                Console.Error.WriteLine($"timeit: {ex.Message}");
            }
            return ExitCode.NotFound;
        }
        catch (InvalidOperationException ex)
        {
            // Unexpected process start failure (bad EXE format, out of memory, etc.)
            if (jsonOutput)
            {
                Console.Error.WriteLine(Formatting.FormatJsonError(ExitCode.UsageError, "start_error", "timeit", version));
            }
            else
            {
                Console.Error.WriteLine($"timeit: {ex.Message}");
            }
            return ExitCode.UsageError;
        }

        string output;
        if (jsonOutput)
        {
            output = Formatting.FormatJson(timeResult, "timeit", version);
        }
        else if (oneLine)
        {
            output = Formatting.FormatOneLine(timeResult, useColor);
        }
        else
        {
            output = Formatting.FormatDefault(timeResult, useColor);
        }

        writer.WriteLine(output);

        return timeResult.ExitCode;
    }

    private static string GetVersion()
    {
        return typeof(TimeItResult).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
    }
}
