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
            Console.Error.WriteLine($"envvault: key store unavailable: {ex.Message}");
            return ExitCode.NotExecutable;
        }

        IProcessLauncher launcher = new DefaultProcessLauncher();
        IConsolePrompt prompt = new DefaultConsolePrompt();

        return Cli.Run(args, store, launcher, prompt, Console.Out, Console.Error);
    }
}
