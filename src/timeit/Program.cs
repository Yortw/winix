using System.Reflection;
using Winix.TimeIt;
using Yort.ShellKit;

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
        (ExitCode.NotFound, "Command not found"));

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
    if (jsonOutput)
    {
        writer.WriteLine(Formatting.FormatJsonError(ExitCode.UsageError, "usage_error", "timeit", version));
    }
    else
    {
        Console.Error.WriteLine("timeit: no command specified. Run 'timeit --help' for usage.");
    }
    return ExitCode.UsageError;
}

string command = result.Command[0];
string[] commandArgs = result.Command.Skip(1).ToArray();

// Run the command
TimeItResult timeResult;
try
{
    timeResult = CommandRunner.Run(command, commandArgs);
}
catch (CommandNotExecutableException ex)
{
    if (jsonOutput)
    {
        writer.WriteLine(Formatting.FormatJsonError(ExitCode.NotExecutable, "command_not_executable", "timeit", version));
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
        writer.WriteLine(Formatting.FormatJsonError(ExitCode.NotFound, "command_not_found", "timeit", version));
    }
    else
    {
        Console.Error.WriteLine($"timeit: {ex.Message}");
    }
    return ExitCode.NotFound;
}

// Format and write output
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

static string GetVersion()
{
    return typeof(TimeItResult).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion ?? "0.0.0";
}
