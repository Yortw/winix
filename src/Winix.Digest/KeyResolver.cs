#nullable enable
using System;
using System.IO;
using System.Text;

namespace Winix.Digest;

/// <summary>Discriminates where an HMAC key comes from.</summary>
public abstract record KeySource
{
    /// <summary>Key bytes read from the named environment variable (UTF-8 encoded).</summary>
    public static KeySource EnvVariable(string name) => new EnvSource(name);

    /// <summary>Key bytes read from the named file. Trailing newline is stripped unless <c>--key-raw</c>.</summary>
    public static KeySource File(string path) => new FileSource(path);

    /// <summary>Key bytes read from stdin. Trailing newline is stripped unless <c>--key-raw</c>.</summary>
    public static KeySource Stdin() => new StdinSource();

    /// <summary>Key bytes from a literal string (UTF-8 encoded). Emits a stderr warning — key is exposed to <c>ps</c> and shell history.</summary>
    public static KeySource Literal(string value) => new LiteralSource(value);

    /// <summary>Env-variable key source produced by <see cref="EnvVariable"/>.</summary>
    public sealed record EnvSource(string Name) : KeySource;
    /// <summary>File key source produced by <see cref="File"/>.</summary>
    public sealed record FileSource(string Path) : KeySource;
    /// <summary>Stdin key source produced by <see cref="Stdin"/>.</summary>
    public sealed record StdinSource : KeySource;
    /// <summary>Literal key source produced by <see cref="Literal"/>.</summary>
    public sealed record LiteralSource(string Value) : KeySource;
}

/// <summary>
/// Resolves an HMAC key byte sequence from one of four sources (env, file, stdin, literal),
/// emitting security warnings to stderr where appropriate.
/// </summary>
public static class KeyResolver
{
    private const string LiteralWarning =
        "digest: warning: --key exposes the key via 'ps', shell history, and process listings.\n" +
        "        Prefer --key-env, --key-file, or --key-stdin for non-ephemeral scripts.";

    /// <summary>
    /// Resolves the key bytes. Returns null on error; the <paramref name="error"/> out-param
    /// carries the user-facing message (for the console app to format and exit with code 125).
    /// </summary>
    /// <param name="source">Which of the four sources to read the key from.</param>
    /// <param name="stdin">TextReader used when <paramref name="source"/> is <see cref="KeySource.StdinSource"/>; tests inject a fake reader.</param>
    /// <param name="stripTrailingNewline">When true, a single trailing "\n" or "\r\n" is removed from file/stdin sources (so a user who does <c>echo secret &gt; file</c> doesn't accidentally include the newline in their key). Literal and env sources are never stripped.</param>
    /// <param name="stderr">TextWriter for security warnings (literal key, group/other-readable file).</param>
    /// <param name="error">Out-param: user-facing error message, or null on success.</param>
    /// <returns>Key bytes on success; null on failure (see <paramref name="error"/>).</returns>
    public static byte[]? Resolve(
        KeySource source,
        TextReader stdin,
        bool stripTrailingNewline,
        TextWriter stderr,
        out string? error)
    {
        error = null;

        switch (source)
        {
            case KeySource.EnvSource env:
                string? value = Environment.GetEnvironmentVariable(env.Name);
                if (value is null)
                {
                    error = $"environment variable '{env.Name}' is not set";
                    return null;
                }
                return Encoding.UTF8.GetBytes(value);

            case KeySource.FileSource file:
                if (!File.Exists(file.Path))
                {
                    error = $"key file '{file.Path}' not found";
                    return null;
                }
                string? permWarning = KeyFilePermissionsCheck.GetWarningOrNull(file.Path);
                if (permWarning is not null)
                {
                    stderr.WriteLine(permWarning);
                }
                byte[] fileBytes = File.ReadAllBytes(file.Path);
                return stripTrailingNewline ? StripOneTrailingNewline(fileBytes) : fileBytes;

            case KeySource.StdinSource:
                string stdinText = stdin.ReadToEnd();
                byte[] stdinBytes = Encoding.UTF8.GetBytes(stdinText);
                return stripTrailingNewline ? StripOneTrailingNewline(stdinBytes) : stdinBytes;

            case KeySource.LiteralSource literal:
                stderr.WriteLine(LiteralWarning);
                return Encoding.UTF8.GetBytes(literal.Value);

            default:
                throw new ArgumentOutOfRangeException(nameof(source), source, null);
        }
    }

    // Strip a single trailing LF or CRLF. Indexed access (not range syntax) per project style.
    private static byte[] StripOneTrailingNewline(byte[] bytes)
    {
        int len = bytes.Length;
        if (len >= 2 && bytes[len - 2] == (byte)'\r' && bytes[len - 1] == (byte)'\n')
        {
            byte[] trimmed = new byte[len - 2];
            Array.Copy(bytes, 0, trimmed, 0, len - 2);
            return trimmed;
        }
        if (len >= 1 && bytes[len - 1] == (byte)'\n')
        {
            byte[] trimmed = new byte[len - 1];
            Array.Copy(bytes, 0, trimmed, 0, len - 1);
            return trimmed;
        }
        return bytes;
    }
}
