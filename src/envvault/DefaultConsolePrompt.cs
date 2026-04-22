#nullable enable
using System;
using Winix.EnvVault;

namespace EnvVault;

/// <summary>Real-world <see cref="IConsolePrompt"/>: Console.ReadKey for echo-off tty, Console.In.ReadLine for piped stdin.</summary>
internal sealed class DefaultConsolePrompt : IConsolePrompt
{
    public bool IsInteractive => !Console.IsInputRedirected;

    public void WritePrompt(string text) => Console.Error.Write(text);

    public string ReadLineEchoOff()
    {
        // State machine lives in Winix.EnvVault.KeyAccumulator so it can be unit-tested without
        // a real tty. This method owns only the actual Console.ReadKey loop and the Console.Error
        // newline writes on Submit/Cancel — two things that cannot be usefully unit-tested.
        KeyAccumulator acc = new();
        while (true)
        {
            KeyOutcome outcome = acc.Apply(Console.ReadKey(intercept: true));
            switch (outcome)
            {
                case KeyOutcome.Submit:
                    Console.Error.WriteLine();
                    return acc.Current;
                case KeyOutcome.Cancel:
                    Console.Error.WriteLine();
                    throw new OperationCanceledException("interrupted by user (Ctrl+C)");
                case KeyOutcome.Edit:
                case KeyOutcome.Ignore:
                    continue;
            }
        }
    }

    public string? ReadLineFromStdin() => Console.In.ReadLine();
}
