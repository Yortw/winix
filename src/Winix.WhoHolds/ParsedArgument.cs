#nullable enable

namespace Winix.WhoHolds;

/// <summary>
/// The result of parsing a single command-line argument into either a file path,
/// a port number, or an error.
/// </summary>
public sealed class ParsedArgument
{
    /// <summary>
    /// The resolved file or directory path, or <see langword="null"/> if the argument
    /// was not resolved as a filesystem path.
    /// </summary>
    public string? FilePath { get; }

    /// <summary>
    /// The resolved port number, or 0 if the argument was not resolved as a port.
    /// Only meaningful when <see cref="IsPort"/> is <see langword="true"/>.
    /// </summary>
    public int Port { get; }

    /// <summary>
    /// The error message describing why parsing failed, or <see langword="null"/>
    /// if parsing succeeded.
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// <see langword="true"/> when the argument resolved to a filesystem path
    /// (file or directory).
    /// </summary>
    public bool IsFile => FilePath is not null;

    /// <summary>
    /// <see langword="true"/> when the argument resolved to a valid port number
    /// (1–65535) and was not interpreted as a filesystem path.
    /// </summary>
    public bool IsPort => Port > 0 && FilePath is null && ErrorMessage is null;

    /// <summary>
    /// <see langword="true"/> when parsing failed and <see cref="ErrorMessage"/>
    /// contains the reason.
    /// </summary>
    public bool IsError => ErrorMessage is not null;

    private ParsedArgument(string? filePath, int port, string? errorMessage)
    {
        FilePath = filePath;
        Port = port;
        ErrorMessage = errorMessage;
    }

    /// <summary>Creates a result representing a resolved filesystem path.</summary>
    /// <param name="filePath">The resolved file or directory path.</param>
    internal static ParsedArgument ForFile(string filePath)
    {
        return new ParsedArgument(filePath, 0, null);
    }

    /// <summary>Creates a result representing a resolved port number.</summary>
    /// <param name="port">The port number (1–65535).</param>
    internal static ParsedArgument ForPort(int port)
    {
        return new ParsedArgument(null, port, null);
    }

    /// <summary>Creates a result representing a parse error.</summary>
    /// <param name="message">A human-readable description of the error.</param>
    internal static ParsedArgument Error(string message)
    {
        return new ParsedArgument(null, 0, message);
    }
}
