using Winix.TimeIt;
using Yort.ShellKit;

namespace TimeIt;

internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();

        // Round-1 review CR-I1 / TA-C1 — Program.cs is now a thin shim around Cli.Run.
        // All orchestration (parse, dispatch on exception type, format, exit code resolution)
        // lives in the library so per-error-type exit codes and the --stdout writer-selection
        // contract are unit-testable. Mirrors the digest/notify/url Cli seam pattern.
        return Cli.Run(args, System.Console.Out, System.Console.Error);
    }
}
