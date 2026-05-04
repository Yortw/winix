using System;
using Winix.Url;
using Yort.ShellKit;

namespace Url;

internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();

        // Round-1 review TA-C1 — Program.cs is now a thin shim around Cli.Run. All
        // orchestration (parse, dispatch on subcommand, format, exit code resolution)
        // lives in the library so per-subcommand exit-code paths and the --field /
        // join-error / generic-catch branches are unit-testable.
        return Cli.Run(args, Console.Out, Console.Error);
    }
}
