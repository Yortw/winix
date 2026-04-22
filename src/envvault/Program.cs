#nullable enable
using System;
using Winix.EnvVault;
using Winix.SecretStore;
using Yort.ShellKit;

namespace EnvVault;

internal sealed class Program
{
    /// <summary>Console entry point. Wires the real IO adapters and delegates to <see cref="Cli.Run"/>.</summary>
    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();

        ISecretStore store;
        try
        {
            store = SecretStoreFactory.CreateUserStore();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // Unwrap TypeInitializationException via the shared Cli helper so the user sees
            // the actionable inner message ("Unable to load libsecret-1.so.0") rather than
            // the .NET wrapper text ("The type initializer for X threw an exception.").
            SafeWriteLine(Console.Error, $"envvault: key store unavailable: {Cli.UnwrapTypeInit(ex).Message}");
            return ExitCode.NotExecutable;
        }

        IProcessLauncher launcher = new DefaultProcessLauncher();
        IConsolePrompt prompt = new DefaultConsolePrompt();

        // Final safety net: Cli.Run's internal handlers use SafeWriteLine so stderr failures
        // never escape — but if anything at all leaks (e.g. a future handler forgetting the
        // guard), catch here so Main always returns a POSIX-shaped exit code rather than
        // letting the CLR emit a stack trace.
        try
        {
            return Cli.Run(args, store, launcher, prompt, Console.Out, Console.Error,
                stdoutIsTty: !Console.IsOutputRedirected);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            SafeWriteLine(Console.Error, $"envvault: {Cli.UnwrapTypeInit(ex).Message}");
            return ExitCode.NotExecutable;
        }
    }

    /// <summary>Best-effort stderr write. Mirrors <c>Cli.SafeWriteLine</c> so Program.Main's bootstrap and final-safety paths are also broken-stderr-tolerant.</summary>
    private static void SafeWriteLine(System.IO.TextWriter writer, string message)
    {
        try { writer.WriteLine(message); } catch { /* diagnostic must never fail the caller */ }
    }
}
