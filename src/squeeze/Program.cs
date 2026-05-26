// src/squeeze/Program.cs
using Winix.Squeeze;
using Yort.ShellKit;

namespace Squeeze;

internal sealed class Program
{
    static async Task<int> Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();

        // Round-1 review CR-I1 / TA-C1: orchestration moved to Cli.RunAsync so every dispatch
        // path is testable without spawning a real process. Program.cs is the standard thin
        // shim — it only resolves real-process console state and the binary stdin/stdout
        // streams.
        bool stdinRedirected = Console.IsInputRedirected;
        bool stdoutIsTerminal = ConsoleEnv.IsTerminal(checkStdErr: true);

        using Stream stdin = Console.OpenStandardInput();
        using Stream stdout = Console.OpenStandardOutput();

        return await Cli.RunAsync(
            args,
            stdin: stdin,
            stdout: stdout,
            stderr: Console.Error,
            stdinIsRedirected: stdinRedirected,
            stdoutIsTerminal: stdoutIsTerminal);
    }
}
