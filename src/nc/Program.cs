#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Winix.NetCat;
using Yort.ShellKit;

namespace Nc;

internal sealed class Program
{
    /// <summary>
    /// Entry point. Owns process-global state only: console setup and Ctrl+C registration.
    /// All parsing, validation, and mode dispatch live in <see cref="Cli.RunAsync"/>.
    /// </summary>
    static async Task<int> Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();

        // Named handler + finally-unregister so Ctrl+C arriving during shutdown can't fire a
        // handler that calls Cancel on a disposed CTS. Same pattern as retry/envvault.
        // Registration moved here from the old DispatchAsync (seam ADR N3): process-global
        // console state is Main's responsibility; the seam observes only the token.
        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            e.Cancel = true;
            try { cts.Cancel(); } catch (ObjectDisposedException) { /* raced with shutdown */ }
        };
        Console.CancelKeyPress += cancelHandler;
        try
        {
            return await Cli.RunAsync(args, Console.OpenStandardInput(), Console.OpenStandardOutput(),
                Console.Error, cts.Token).ConfigureAwait(false);
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }
}
