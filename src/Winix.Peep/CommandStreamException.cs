namespace Winix.Peep;

/// <summary>
/// Thrown when reading the child process's stdout/stderr streams fails — typically
/// because the child closed a pipe abnormally, the OS handle became invalid, or a
/// stream-level <see cref="IOException"/> escaped <see cref="System.IO.StreamReader.ReadAsync(char[], int, int)"/>.
/// peep catches this in its watch loop and surfaces a clean error envelope rather
/// than crashing the alternate-screen-buffer session with a stack trace.
/// </summary>
public sealed class CommandStreamException : Exception
{
    /// <summary>
    /// Initialises with a message and the underlying stream-read exception.
    /// </summary>
    public CommandStreamException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
