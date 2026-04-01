namespace Yort.ShellKit;

/// <summary>
/// Thrown when the specified command cannot be found on PATH.
/// </summary>
public sealed class CommandNotFoundException : Exception
{
    /// <inheritdoc />
    public CommandNotFoundException(string command)
        : base($"command not found: {command}")
    {
        Command = command;
    }

    /// <summary>
    /// The command name that could not be found.
    /// </summary>
    public string Command { get; }
}
