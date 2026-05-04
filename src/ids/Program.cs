using System;
using Winix.Ids;
using Yort.ShellKit;

namespace Ids;

internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();

        // Round-1 review CR-I1 / TA-C1 — Program.cs is now a thin shim around Cli.Run.
        // All orchestration (parse, generate, format, exit code resolution) lives in the
        // library so tests can pin the streaming-JSON shape, IOException pipe-close
        // handling, and catch-all error path. Mirrors digest/notify/url/timeit pattern.
        return Cli.Run(args, Console.Out, Console.Error);
    }
}
