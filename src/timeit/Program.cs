using System.Reflection;
using Winix.TimeIt;

return Run(args);

static int Run(string[] args)
{
    bool colorFlag = false;
    bool noColorFlag = false;
    bool oneLine = false;
    bool jsonOutput = false;
    bool useStdout = false;
    int commandStart = -1;

    // Parse timeit flags, stop at first unrecognised argument or --
    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--color":
                colorFlag = true;
                break;
            case "--no-color":
                noColorFlag = true;
                break;
            case "-1":
            case "--oneline":
                oneLine = true;
                break;
            case "--json":
                jsonOutput = true;
                break;
            case "--stdout":
                useStdout = true;
                break;
            case "--version":
                Console.WriteLine($"timeit {GetVersion()}");
                return 0;
            case "-h":
            case "--help":
                PrintHelp();
                return 0;
            case "--":
                commandStart = i + 1;
                break;
            default:
                commandStart = i;
                break;
        }

        if (commandStart >= 0)
        {
            break;
        }
    }

    string version = GetVersion();
    TextWriter writer = useStdout ? Console.Out : Console.Error;

    if (commandStart < 0 || commandStart >= args.Length)
    {
        if (jsonOutput)
        {
            writer.WriteLine(Formatting.FormatJsonError(125, "usage_error", "timeit", version));
        }
        else
        {
            Console.Error.WriteLine("timeit: no command specified. Run 'timeit --help' for usage.");
        }
        return 125;
    }

    string command = args[commandStart];
    string[] commandArgs = args.Skip(commandStart + 1).ToArray();

    // Resolve colour
    bool noColorEnv = ConsoleEnv.IsNoColorEnvSet();
    bool isTerminal = ConsoleEnv.IsTerminal(checkStdErr: !useStdout);
    bool useColor = ConsoleEnv.ResolveUseColor(colorFlag, noColorFlag, noColorEnv, isTerminal);

    // Run the command
    TimeItResult result;
    try
    {
        result = CommandRunner.Run(command, commandArgs);
    }
    catch (CommandNotExecutableException ex)
    {
        if (jsonOutput)
        {
            writer.WriteLine(Formatting.FormatJsonError(126, "command_not_executable", "timeit", version));
        }
        else
        {
            Console.Error.WriteLine($"timeit: {ex.Message}");
        }
        return 126;
    }
    catch (CommandNotFoundException ex)
    {
        if (jsonOutput)
        {
            writer.WriteLine(Formatting.FormatJsonError(127, "command_not_found", "timeit", version));
        }
        else
        {
            Console.Error.WriteLine($"timeit: {ex.Message}");
        }
        return 127;
    }

    // Format and write output
    string output;
    if (jsonOutput)
    {
        output = Formatting.FormatJson(result, "timeit", version);
    }
    else if (oneLine)
    {
        output = Formatting.FormatOneLine(result, useColor);
    }
    else
    {
        output = Formatting.FormatDefault(result, useColor);
    }

    writer.WriteLine(output);

    return result.ExitCode;
}

static void PrintHelp()
{
    Console.WriteLine(
        """
        Usage: timeit [options] [--] <command> [args...]

        Time a command and show wall clock, CPU time, peak memory, and exit code.

        Options:
          -1, --oneline       Single-line output format
          --json              JSON output format
          --stdout            Write summary to stdout instead of stderr
          --no-color          Disable colored output
          --color             Force colored output (even when piped)
          --version           Show version
          -h, --help          Show help

        Exit Codes:
          <N>                 Child process exit code (pass-through)
          125                 No command specified or bad timeit arguments
          126                 Command not executable (permission denied)
          127                 Command not found
        """);
}

static string GetVersion()
{
    return typeof(TimeItResult).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion ?? "0.0.0";
}
