namespace Yort.ShellKit;

/// <summary>
/// Thrown when the specified command exists but cannot be executed (e.g. permission denied).
/// </summary>
public sealed class CommandNotExecutableException : Exception
{
    /// <summary>
    /// Initialises with the command name that could not be executed.
    /// </summary>
    public CommandNotExecutableException(string command)
        : base($"permission denied: {command}")
    {
        Command = command;
    }

    /// <summary>
    /// Initialises with a message and inner exception for re-throw scenarios.
    /// </summary>
    public CommandNotExecutableException(string message, Exception innerException)
        : base(message, innerException)
    {
        Command = string.Empty;
    }

    /// <summary>
    /// The command that could not be executed.
    /// </summary>
    public string Command { get; }
}
