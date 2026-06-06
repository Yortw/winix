using Winix.Wargs;
using Yort.ShellKit;

namespace Wargs;

internal sealed class Program
{
    /// <summary>
    /// Entry point. Owns process-global state only: console setup, Ctrl+C registration,
    /// and the real-console stdin-unblock-on-cancel hook. All parsing, validation, the
    /// job pipeline, and the envelope-on-every-exit-path catches live in
    /// <see cref="Cli.RunAsync"/>.
    /// </summary>
    static async Task<int> Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();

        // Register Console.CancelKeyPress AT MAIN SCOPE, BEFORE RunAsync — including before
        // input materialisation. Round-4 wrongly assumed an in-RunAsync handler caught
        // Ctrl+C-during-stdin-read; in fact .NET's default Ctrl+C handling tears the
        // process down before any handler installed later in the call stack can fire.
        // With this early registration, e.Cancel=true keeps the process alive long enough
        // for InputReader to observe the cancellation (Console.In.ReadLine returns null
        // after Ctrl+C with e.Cancel=true) and for the OCE catch (now in Cli.RunAsync) to
        // emit the cancelled envelope.
        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            e.Cancel = true;
            // Round-7 SFH: broaden from ObjectDisposedException to all exceptions.
            // CancellationTokenSource.Cancel() invokes registered callbacks synchronously
            // and aggregates any callback throws into AggregateException. A SIGINT handler
            // that throws ANY exception lets the runtime tear the process down — bypassing
            // the entire envelope-emission catch in Cli.RunAsync. Best-effort: never crash
            // the process from the SIGINT handler.
            try { cts.Cancel(); }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException) { /* see comment */ }
        };
        Console.CancelKeyPress += cancelHandler;

        // Round-7 SFH C1/I1: a blocked Console.In.ReadLine() on Linux may not return null
        // after `e.Cancel=true` (the runtime may translate EINTR to IOException, restart
        // the read, or block indefinitely depending on the .NET runtime version). The
        // round-6 fix assumed null-return — fragile on Linux. Closing stdin on cancel
        // forces the read to unblock with EOF; the InputReader's cancellation-aware
        // enumerator (round-7) then propagates OCE before the empty-input branch fires.
        //
        // Coverage caveat: this is end-to-end pinned for Linux only via SkippableFact
        // CtrlCDuringStdin_UnderNdjson_EmitsCancelledEnvelope. On Windows, SyncTextReader
        // wraps Close() with `lock(this)` — same lock the read holds — so cross-thread
        // close from the SIGINT handler is a documented synchronization concern. The
        // fallback Windows behaviour (CancelKeyPress + e.Cancel=true alone returning null
        // from ReadLine) is empirically observed to work for the dev-box smoke test but
        // not regression-pinned. Round-8 SFH I2 / TA I2 — left as a known coverage gap;
        // see project_wargs_progress.md for the planned Windows GenerateConsoleCtrlEvent
        // integration test.
        //
        // This registration stays in Main (seam ADR W2): it mutates the REAL console's
        // stdin; Cli.RunAsync's injected reader must never be conflated with Console.In.
        cts.Token.Register(() =>
        {
            try { Console.In.Close(); }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException) { /* best-effort — Console.In may already be closed */ }
        });

        try
        {
            return await Cli.RunAsync(args, Console.In, Console.Out, Console.Error, cts.Token).ConfigureAwait(false);
        }
        finally
        {
            // Unregister before 'using' disposes cts, so a late Ctrl+C
            // doesn't call Cancel() on a disposed CancellationTokenSource.
            Console.CancelKeyPress -= cancelHandler;
        }
    }
}
