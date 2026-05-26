#nullable enable

using Winix.Less;
using Yort.ShellKit;

namespace Less;

/// <summary>
/// Thin shim around <see cref="Cli.Run"/>. All orchestration lives in the library so it
/// can be exercised without spawning a process or entering the interactive pager loop;
/// this entry point just wires up <c>Console.*</c> and forwards the exit code.
/// </summary>
internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();
        return Cli.Run(
            args,
            Console.Out,
            Console.Error,
            Console.IsOutputRedirected,
            Console.IsInputRedirected);
    }
}
