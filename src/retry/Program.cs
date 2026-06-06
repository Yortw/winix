using System;
using System.Threading;
using Winix.Retry;
using Yort.ShellKit;

namespace Retry;

internal sealed class Program
{
    /// <summary>
    /// Entry point. Owns process-global state only: console setup and Ctrl+C handling.
    /// All parsing, validation, and the retry loop live in <see cref="Cli.Run"/>.
    /// </summary>
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();

        // Ctrl+C stays in Main: Console.CancelKeyPress is a process-global static event that
        // doesn't compose with xunit parallelism, and CTS disposal is entry-point lifetime
        // management. The CancelKeyPress handler must be named + unregistered in finally.
        // A captured-by-closure anonymous handler keeps a reference to the CTS past the
        // using-scope exit; a second Ctrl+C during AOT teardown would call Cancel() on a
        // disposed CTS and ObjectDisposedException would escape as a shutdown crash.
        // Reference fix: src/wargs/Program.cs:172-185.
        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            e.Cancel = true;
            try { cts.Cancel(); }
            catch (ObjectDisposedException) { /* raced with shutdown — safe to drop */ }
        };
        Console.CancelKeyPress += cancelHandler;
        try
        {
            return Cli.Run(args, Console.Out, Console.Error, cts.Token);
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }
}
