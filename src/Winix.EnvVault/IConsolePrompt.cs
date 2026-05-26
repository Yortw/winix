#nullable enable
namespace Winix.EnvVault;

/// <summary>Abstraction over tty interactions for testable prompting.</summary>
public interface IConsolePrompt
{
    /// <summary>True if stdin is a terminal (interactive), false if piped.</summary>
    bool IsInteractive { get; }
    /// <summary>Write the prompt banner to stderr.</summary>
    void WritePrompt(string text);
    /// <summary>Read a line from the console with echo off. Only called when <see cref="IsInteractive"/> is true.</summary>
    string ReadLineEchoOff();
    /// <summary>Read a line from stdin normally. Only called when <see cref="IsInteractive"/> is false.</summary>
    string? ReadLineFromStdin();
}
