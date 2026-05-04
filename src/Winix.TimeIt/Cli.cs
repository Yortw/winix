#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Yort.ShellKit;

namespace Winix.TimeIt;

/// <summary>
/// Library-level entry point for the timeit CLI. Program.cs is a thin shim around
/// <see cref="Run"/>; all behaviour lives here so it can be exercised by unit tests
/// (per-exception-type exit codes, JSON-vs-plain error formatting, the
/// <c>--stdout</c> writer-selection contract, exit-code passthrough).
/// </summary>
/// <remarks>
/// Round-1 review CR-I1 / TA-C1 — pin the user-visible per-error-type exit-code matrix
/// (declared in BuildParser ExitCodes(...)) and the "errors always go to stderr even
/// when --stdout redirects summary" invariant without spawning processes. Matches the
/// digest/notify/url Cli seam pattern.
/// </remarks>
public static class Cli
{
    /// <summary>
    /// Runs the timeit pipeline: parse args, spawn child, format, return exit code.
    /// </summary>
    /// <param name="args">CLI argument vector (without the executable name).</param>
    /// <param name="stdout">stdout writer; production: <see cref="Console.Out"/>.</param>
    /// <param name="stderr">stderr writer; production: <see cref="Console.Error"/>.</param>
    /// <returns>Exit code: child's exit code on success; <c>ExitCode.UsageError</c> (125) for argument errors;
    /// <c>ExitCode.NotExecutable</c> (126) for permission denied or bad-EXE-format; <c>ExitCode.NotFound</c> (127) for command-not-found.</returns>
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        string version = GetVersion();
        var parser = BuildParser(version);

        var result = parser.Parse(args);
        if (result.IsHandled) return result.ExitCode;
        if (result.HasErrors) return result.WriteErrors(stderr);

        bool oneLine = result.Has("--oneline");
        bool jsonOutput = result.Has("--json");
        bool useStdout = result.Has("--stdout");
        bool useColor = result.ResolveColor(checkStdErr: !useStdout);
        TextWriter writer = useStdout ? stdout : stderr;

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
            // Errors always go to stderr, even when --stdout redirects the summary.
            if (jsonOutput)
            {
                stderr.WriteLine(Formatting.FormatJsonError(ExitCode.NotExecutable, "command_not_executable", "timeit", version));
            }
            else
            {
                stderr.WriteLine($"timeit: {ex.Message}");
            }
            return ExitCode.NotExecutable;
        }
        catch (CommandNotFoundException ex)
        {
            if (jsonOutput)
            {
                stderr.WriteLine(Formatting.FormatJsonError(ExitCode.NotFound, "command_not_found", "timeit", version));
            }
            else
            {
                stderr.WriteLine($"timeit: {ex.Message}");
            }
            return ExitCode.NotFound;
        }
        catch (InvalidOperationException ex)
        {
            // Round-1 review SFH-Minor-2 — bad EXE format / unexpected process-start failure
            // is an environment failure (the binary is corrupt or the OS refused to load it),
            // NOT a usage error. POSIX precedent: this maps to exit 126 (NotExecutable), not
            // 125 (UsageError). The previous mapping silently confused "I called timeit
            // wrong" with "the binary is corrupt" for scripts dispatching on exit code.
            if (jsonOutput)
            {
                stderr.WriteLine(Formatting.FormatJsonError(ExitCode.NotExecutable, "start_error", "timeit", version));
            }
            else
            {
                stderr.WriteLine($"timeit: {ex.Message}");
            }
            return ExitCode.NotExecutable;
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

    private static CommandLineParser BuildParser(string version)
    {
        return new CommandLineParser("timeit", version)
            .Description("Time a command and show wall clock, CPU time, peak memory, and exit code.")
            .StandardFlags()
            .Flag("--oneline", "-1", "Single-line output format")
            .Flag("--stdout", "Write summary to stdout instead of stderr")
            .CommandMode()
            .ExitCodes(
                (0, "Child process exit code (pass-through)"),
                (ExitCode.UsageError, "No command specified or bad timeit arguments"),
                (ExitCode.NotExecutable, "Command not executable (permission denied) or bad-EXE-format"),
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
            .JsonField("exit_code", "int", "Tool exit code (0 = success). Distinct from child_exit_code below.")
            .JsonField("exit_reason", "string", "Machine-readable exit reason: success, command_not_found, command_not_executable, start_error.")
            .JsonField("child_exit_code", "int|null", "Child process exit code (the code the child process itself returned). null when the child never ran (e.g. command_not_found).")
            .JsonField("wall_seconds", "float", "Wall clock time in seconds")
            .JsonField("user_cpu_seconds", "float|null", "User CPU time in seconds")
            .JsonField("sys_cpu_seconds", "float|null", "System CPU time in seconds")
            .JsonField("cpu_seconds", "float|null", "Total CPU time (user + sys)")
            .JsonField("peak_memory_bytes", "int|null", "Peak working set in bytes");
    }

    private static string GetVersion()
    {
        return typeof(TimeItResult).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
    }
}
