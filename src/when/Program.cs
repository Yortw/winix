using Winix.When;
using Yort.ShellKit;

namespace When;

internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();

        // Round-1 review CR-I3 / TA-C1 — Program.cs is now a thin shim around Cli.Run.
        // All orchestration (mode dispatch, mutual-exclusion, error envelopes, the
        // negative-offset injector) lives in the library so contracts are testable.
        return Cli.Run(args, System.Console.Out, System.Console.Error);
    }
}
