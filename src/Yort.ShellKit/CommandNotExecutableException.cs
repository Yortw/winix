namespace Yort.ShellKit;

/// <summary>
/// Thrown when the specified command exists but cannot be executed (e.g. permission denied).
/// </summary>
public sealed class CommandNotExecutableException : Exception
{
    /// <inheritdoc />
    public CommandNotExecutableException(string command)
        : base($"permission denied: {command}")
    {
        Command = command;
    }

    /// <summary>
    /// The command that could not be executed.
    /// </summary>
    public string Command { get; }
}
