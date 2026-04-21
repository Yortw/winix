#nullable enable
using System;
using System.Text;
using Winix.EnvVault;

namespace EnvVault;

/// <summary>Real-world <see cref="IConsolePrompt"/>: Console.ReadKey for echo-off tty, Console.In.ReadLine for piped stdin.</summary>
internal sealed class DefaultConsolePrompt : IConsolePrompt
{
    public bool IsInteractive => !Console.IsInputRedirected;

    public void WritePrompt(string text) => Console.Error.Write(text);

    public string ReadLineEchoOff()
    {
        StringBuilder sb = new();
        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.Error.WriteLine();
                return sb.ToString();
            }
            if (key.Key == ConsoleKey.Backspace && sb.Length > 0)
            {
                sb.Length--;
                continue;
            }
            if (!char.IsControl(key.KeyChar))
            {
                sb.Append(key.KeyChar);
            }
        }
    }

    public string? ReadLineFromStdin() => Console.In.ReadLine();
}
