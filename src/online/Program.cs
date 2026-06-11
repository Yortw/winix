using System;
using System.Threading;
using Winix.Online;
using Yort.ShellKit;

namespace Online;

internal sealed class Program
{
    /// <summary>
    /// Entry point. Owns process-global state only: console setup and Ctrl+C handling.
    /// All parsing, validation, and the wait loop live in <see cref="Cli.RunAsync"/>.
    /// </summary>
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();

        // Ctrl+C stays in Main: Console.CancelKeyPress is a process-global static event. The named
        // handler is unregistered in finally; the catch guards a second Ctrl+C racing CTS disposal
        // during AOT teardown. Reference: src/retry/Program.cs.
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
            return Cli.RunAsync(args, Console.Out, Console.Error, cts.Token).GetAwaiter().GetResult();
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }
}
