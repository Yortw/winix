#nullable enable

using System;
using Winix.Schedule;
using Yort.ShellKit;

namespace Schedule;

internal sealed class Program
{
    /// <summary>
    /// Entry point. Owns process-global console setup only; all parsing, dispatch, and
    /// output routing live in <see cref="Cli.Run"/> so they are testable in-process.
    /// </summary>
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();
        return Cli.Run(args, Console.Out, Console.Error);
    }
}
