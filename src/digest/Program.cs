using System;
using Winix.Digest;
using Yort.ShellKit;

namespace Digest;

internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();

        // Round-2 — Program.cs is now a thin shim around Cli.Run. All orchestration lives
        // in the library so tests can exercise it without process spawning. The byte-stream
        // payload-stdin (CR-I3) is wired here via Console.OpenStandardInput().
        return Cli.Run(
            args: args,
            keyStdin: Console.In,
            payloadStdin: Console.OpenStandardInput(),
            stdout: Console.Out,
            stderr: Console.Error);
    }
}
