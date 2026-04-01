namespace Yort.ShellKit;

/// <summary>
/// Thrown when the specified command cannot be found on PATH.
/// </summary>
public sealed class CommandNotFoundException : Exception
{
    /// <summary>
    /// Initialises with the command name that could not be found.
    /// </summary>
    public CommandNotFoundException(string command)
        : base($"command not found: {command}")
    {
        Command = command;
    }

    /// <summary>
    /// Initialises with a message and inner exception for re-throw scenarios.
    /// </summary>
    public CommandNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
        Command = string.Empty;
    }

    /// <summary>
    /// The command name that could not be found.
    /// </summary>
    public string Command { get; }
}
