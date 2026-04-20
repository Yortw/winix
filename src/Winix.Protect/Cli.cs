#nullable enable
using System;
using Yort.ShellKit;

namespace Winix.Protect;

/// <summary>
/// Entry point invoked by both the <c>protect</c> and <c>unprotect</c> console apps.
/// Dispatches Protect vs Unprotect based on <paramref name="invocationName"/>.
/// </summary>
public static class Cli
{
    /// <summary>Run the CLI. Returns a process exit code.</summary>
    /// <param name="args">Command-line arguments (without argv[0]).</param>
    /// <param name="invocationName">Either "protect" or "unprotect".</param>
    public static int Run(string[] args, string invocationName)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();
        Console.Error.WriteLine($"{invocationName}: not yet implemented");
        return ExitCode.UsageError;
    }
}
