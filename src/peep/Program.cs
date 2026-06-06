using Winix.Peep;
using Yort.ShellKit;

namespace Peep;

internal sealed class Program
{
    /// <summary>
    /// Entry point. Owns process-global state only: console setup and Ctrl+C registration.
    /// All parsing, validation, and once-mode orchestration live in <see cref="Cli.RunAsync"/>.
    /// </summary>
    static async Task<int> Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();

        // CR I4 / CR I9: once-mode must respect Ctrl+C and not orphan the child. Without our
        // own CTS + CancelKeyPress handler, hitting Ctrl+C during `peep --once -- some-slow-cmd`
        // lets the .NET default handler tear down peep without ever cancelling the token we
        // passed to CommandExecutor — so its kill-on-cancel callback never fires and the child
        // leaks. Registration is now unconditional (Main cannot know once-vs-interactive before
        // Cli.RunAsync parses): during interactive mode this handler is benign — it sets
        // e.Cancel (as the session's own internal handler also does) and cancels a token the
        // interactive path does not observe (ADR P2).
        // R4 TA I6: handler body lives in SessionHelpers.RequestCancellationSilently so the
        // "Cancel after dispose must not throw" contract is regression-pinned
        // (Console.CancelKeyPress is a static event that doesn't compose with xunit).
        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, e)
            => SessionHelpers.RequestCancellationSilently(e, cts);
        Console.CancelKeyPress += cancelHandler;
        try
        {
            return await Cli.RunAsync(args, Console.Out, Console.Error, cts.Token);
        }
        finally
        {
            // Unregister BEFORE the using disposes the CTS, so a late Ctrl+C cannot
            // call Cancel() on a disposed source and surface as ObjectDisposedException
            // (the cancel-handler swallows that, but unregistering first is cleaner).
            Console.CancelKeyPress -= cancelHandler;
        }
    }
}
