#nullable enable
using System.Collections.Generic;
using System.IO;

namespace Winix.EnvVault;

/// <summary>
/// Reads one value per key, either via interactive echo-off prompt (tty) or by reading one line per key
/// from stdin (piped). Throws <see cref="EndOfStreamException"/> if stdin ends before all keys are supplied.
/// </summary>
public sealed class ValuePrompt
{
    private readonly IConsolePrompt _prompt;

    /// <summary>Creates a prompt bound to the given console abstraction.</summary>
    public ValuePrompt(IConsolePrompt prompt) { _prompt = prompt; }

    /// <summary>
    /// Iterates <paramref name="keys"/> and yields each <c>(key, value)</c> pair. Tty mode writes
    /// <c>namespace.key: </c> prompts and reads with echo off. Piped mode reads one line per key;
    /// if stdin ends early, throws <see cref="EndOfStreamException"/> naming the key that was missing.
    /// </summary>
    public IEnumerable<(string Key, string Value)> PromptForKeys(string namespace_, IReadOnlyList<string> keys)
    {
        foreach (string key in keys)
        {
            string value;
            if (_prompt.IsInteractive)
            {
                _prompt.WritePrompt($"{namespace_}.{key}: ");
                value = _prompt.ReadLineEchoOff();
            }
            else
            {
                string? line = _prompt.ReadLineFromStdin();
                if (line == null)
                {
                    throw new EndOfStreamException($"stdin ended before a value for {namespace_}.{key} was provided");
                }
                value = line;
            }
            yield return (key, value);
        }
    }
}
