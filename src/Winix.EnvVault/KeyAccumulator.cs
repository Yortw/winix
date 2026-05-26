#nullable enable
using System;
using System.Text;

namespace Winix.EnvVault;

/// <summary>
/// Pure state machine for the per-keystroke logic of an echo-off line editor. Extracted from
/// <c>DefaultConsolePrompt.ReadLineEchoOff</c> so the branching behaviour — Ctrl+C detection,
/// backspace with underflow guard, control-char filtering, Enter termination — is unit-testable
/// without needing a real <see cref="Console.ReadKey"/> or tty. The real <see cref="IConsolePrompt"/>
/// implementation owns only the actual <see cref="ConsoleKeyInfo"/> read-loop and the stderr prompt
/// write; all behavioural decisions live here.
/// </summary>
/// <remarks>
/// Intentionally does not hold a reference to any IO. Each call to <see cref="Apply"/> takes a
/// single <see cref="ConsoleKeyInfo"/> and reports the outcome via <see cref="KeyOutcome"/>; the
/// caller decides how to act on it. The class accumulates the current buffer internally via a
/// <see cref="StringBuilder"/>; read the buffer via <see cref="Current"/>.
/// </remarks>
internal sealed class KeyAccumulator
{
    private readonly StringBuilder _sb = new();

    /// <summary>Current buffer contents.</summary>
    public string Current => _sb.ToString();

    /// <summary>
    /// Apply one keystroke and return what the caller should do next. Does not itself throw on
    /// Ctrl+C — it reports <see cref="KeyOutcome.Cancel"/> and the caller translates that to an
    /// <see cref="OperationCanceledException"/>.
    /// </summary>
    public KeyOutcome Apply(ConsoleKeyInfo key)
    {
        // Ctrl+C first: on Linux the tty is in raw mode during Console.ReadKey so Ctrl+C arrives
        // as a keystroke, not a SIGINT. Without this branch the char (KeyChar '') would be
        // filtered by the IsControl guard below and the user would be stuck unable to abort.
        if (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key == ConsoleKey.C)
        {
            return KeyOutcome.Cancel;
        }
        if (key.Key == ConsoleKey.Enter)
        {
            return KeyOutcome.Submit;
        }
        if (key.Key == ConsoleKey.Backspace)
        {
            // Underflow guard: at buffer length 0, backspace is a no-op (not a buffer-corruption).
            if (_sb.Length > 0)
            {
                _sb.Length--;
                return KeyOutcome.Edit;
            }
            return KeyOutcome.Ignore;
        }
        // Everything else: accept non-control characters, drop controls. Note: pasted values
        // containing embedded control characters (tabs, CR, LF) will lose those chars silently.
        // This is a tty-raw-mode limitation; the IoC warning in the Set command flagging argv
        // exposure is a partial mitigation.
        if (!char.IsControl(key.KeyChar))
        {
            _sb.Append(key.KeyChar);
            return KeyOutcome.Edit;
        }
        return KeyOutcome.Ignore;
    }
}

/// <summary>Outcome of one keystroke applied to a <see cref="KeyAccumulator"/>.</summary>
internal enum KeyOutcome
{
    /// <summary>Buffer was modified (character appended or backspace removed).</summary>
    Edit,
    /// <summary>Buffer unchanged — the keystroke was a control char we filter, or backspace on an empty buffer.</summary>
    Ignore,
    /// <summary>User pressed Enter. Caller should emit a newline to the prompt-echo stream and return <see cref="KeyAccumulator.Current"/>.</summary>
    Submit,
    /// <summary>User pressed Ctrl+C. Caller should emit a newline and throw <see cref="OperationCanceledException"/>.</summary>
    Cancel,
}
