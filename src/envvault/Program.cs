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

        // Bootstrap-time colour resolution: ArgParser's flag-mode parser hasn't run yet when the
        // store-creation or Cli.Run-leak errors below fire, so consult the environment directly.
        // Matches what ShellKit.ConsoleEnv.ResolveUseColor does for the parsed-flag case.
        bool useColor = !ConsoleEnv.IsNoColorEnvSet() && ConsoleEnv.IsTerminal(checkStdErr: true);

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
            Exception surface = Cli.UnwrapTypeInit(ex);
            // Distinguish "this OS isn't supported at all" from "the OS is supported but the
            // backend is broken". The former is a build/distribution choice, not an availability
            // issue — labelling it 'key store unavailable' misleads the user into checking
            // services/daemons that aren't relevant.
            string label = surface is PlatformNotSupportedException
                ? "platform not supported"
                : "key store unavailable";
            // DescribeSurface (not surface.Message): a framework exception leaking from store creation
            // (e.g. a non-load IOException) would otherwise print a bare SR resource key here. The
            // SAFE classes — SecretStoreException, Win32Exception, PlatformNotSupportedException, and
            // the unwrapped libsecret-load FileNotFoundException — still surface verbatim (ADR row 1).
            SafeWriteLine(Console.Error, Formatting.ErrorLine($"{label}: {Cli.DescribeSurface(surface)}", useColor));
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
            // Final safety net: same SAFE-class routing as Cli's broad catch so a leaked framework
            // exception here doesn't print a bare SR key (ADR row 1).
            SafeWriteLine(Console.Error, Formatting.ErrorLine(Cli.DescribeSurface(Cli.UnwrapTypeInit(ex)), useColor));
            return ExitCode.NotExecutable;
        }
    }

    /// <summary>Best-effort stderr write. Mirrors <c>Cli.SafeWriteLine</c> so Program.Main's bootstrap and final-safety paths are also broken-stderr-tolerant.</summary>
    private static void SafeWriteLine(System.IO.TextWriter writer, string message)
    {
        try { writer.WriteLine(message); } catch { /* diagnostic must never fail the caller */ }
    }
}
