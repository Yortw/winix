// src/qr/Program.cs
#nullable enable
using System;
using System.IO;
using Winix.Qr;
using Yort.ShellKit;

namespace Qr;

internal sealed class Program
{
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();

        // Round-1 review CR-I1 / TA-C1: orchestration moved to Cli.Run so every dispatch path is
        // testable without spawning a real process. Program.cs is the standard thin shim — it only
        // resolves real-process console state (TTY-ness, redirection) and the binary stdout sink.
        bool stdinRedirected = Console.IsInputRedirected;
        bool stdoutIsTty = !Console.IsOutputRedirected;
        using Stream stdoutBinary = Console.OpenStandardOutput();

        return Cli.Run(
            args,
            stdin: Console.In,
            stdout: Console.Out,
            stderr: Console.Error,
            stdoutBinary: stdoutBinary,
            stdinIsRedirected: stdinRedirected,
            stdoutIsTty: stdoutIsTty);
    }
}
