#nullable enable

using Winix.TreeX;
using Yort.ShellKit;

namespace TreeX;

/// <summary>
/// Thin shim around <see cref="Cli.Run"/>. All orchestration lives in the library so it
/// can be exercised without spawning a process; this entry point just wires up
/// <c>Console.*</c> and forwards the exit code.
/// </summary>
internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();
        return Cli.Run(args, Console.Out, Console.Error, Console.IsOutputRedirected);
    }
}
