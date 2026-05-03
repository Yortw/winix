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

    // Round-1 review I3 — cap key reads at 1 MB. HMAC keys are typically 16–128 bytes;
    // an HMAC block-size cap (128 bytes for SHA-512) is the cryptographic ceiling above
    // which the BCL pre-hashes the key, so keys larger than ~1 KB convey no extra entropy.
    // 1 MB is a generous upper bound that still catches the OOM cases this defends against:
    // accidental `--key-file /dev/zero`, `--key-file /proc/kcore`, or piping a multi-GB
    // file via `--key-stdin`. Any legitimate use fits well under the cap.
    internal const int MaxKeySizeBytes = 1024 * 1024;

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

        byte[]? key;
        string sourceLabel;

        switch (source)
        {
            case KeySource.EnvSource env:
                string? value = Environment.GetEnvironmentVariable(env.Name);
                if (value is null)
                {
                    error = $"environment variable '{env.Name}' is not set";
                    return null;
                }
                key = Encoding.UTF8.GetBytes(value);
                sourceLabel = $"environment variable '{env.Name}'";
                break;

            case KeySource.FileSource file:
                if (!File.Exists(file.Path))
                {
                    error = $"key file '{file.Path}' not found";
                    return null;
                }
                // Round-1 review I3 — size-cap the file BEFORE allocation. FileInfo.Length is
                // O(1) on a real file; on /dev/zero / /proc/kcore it returns 0 so the
                // post-read length check below is the actual guard for those cases.
                try
                {
                    long fileSize = new FileInfo(file.Path).Length;
                    if (fileSize > MaxKeySizeBytes)
                    {
                        error = $"key file '{file.Path}' is {fileSize} bytes — exceeds {MaxKeySizeBytes}-byte cap";
                        return null;
                    }
                }
                catch (IOException ex)
                {
                    error = $"failed to stat key file '{file.Path}': {ex.Message}";
                    return null;
                }
                string? permWarning = KeyFilePermissionsCheck.GetWarningOrNull(file.Path);
                if (permWarning is not null)
                {
                    stderr.WriteLine(permWarning);
                }
                // Round-2 review CR-I2/SFH-I1 — scope catches around File.OpenRead the same way
                // HashRunner does for payload reads. A TOCTOU race between FileInfo.Length above
                // and File.OpenRead here, or a permission change, must produce a typed error
                // through this resolver's contract — not escape to Program's outer catch where
                // it gets the wrong (verify-mismatch-shaped) exit code.
                byte[] fileBytes;
                bool fileExceeded;
                try
                {
                    fileBytes = ReadCapped(File.OpenRead(file.Path), MaxKeySizeBytes, out fileExceeded);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    error = $"failed to read key file '{file.Path}': {ex.Message}";
                    return null;
                }
                if (fileExceeded)
                {
                    error = $"key file '{file.Path}' exceeds {MaxKeySizeBytes}-byte cap (read returned more than the size hint)";
                    return null;
                }
                key = stripTrailingNewline ? StripOneTrailingNewline(fileBytes) : fileBytes;
                sourceLabel = $"key file '{file.Path}'";
                break;

            case KeySource.StdinSource:
                // Round-1 review I3 — stdin has no size hint; read up to cap+1 chars then
                // reject if we got cap+1 (which means there was more we'd be discarding).
                // Using a char buffer is correct because TextReader is text-shaped — encoded
                // byte count will be >= char count for non-ASCII so capping chars is a
                // strictly tighter bound than capping bytes.
                byte[] stdinBytes = ReadCappedStdin(stdin, MaxKeySizeBytes, out bool stdinExceeded);
                if (stdinExceeded)
                {
                    error = $"stdin key exceeds {MaxKeySizeBytes}-byte cap";
                    return null;
                }
                key = stripTrailingNewline ? StripOneTrailingNewline(stdinBytes) : stdinBytes;
                sourceLabel = "stdin";
                break;

            case KeySource.LiteralSource literal:
                stderr.WriteLine(LiteralWarning);
                key = Encoding.UTF8.GetBytes(literal.Value);
                sourceLabel = "--key";
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(source), source, null);
        }

        // Round-1 review C1 — reject empty HMAC keys at this layer rather than letting them
        // pass through to BCL HMAC*. The BCL accepts a zero-length key without complaint and
        // produces a deterministic-but-cryptographically-meaningless tag that an attacker can
        // forge instantly. All four key sources can produce a zero-length key (env var set to
        // empty, 0-byte key file, EOF-only stdin, '--key ""'); each path is rejected here with
        // a usage-error message naming the source so the user can correct it. Any newline
        // strip has already happened above, so this is the post-strip length.
        if (key.Length == 0)
        {
            error = $"HMAC key is empty (from {sourceLabel}). " +
                    "An empty HMAC key produces a forgeable tag and is rejected.";
            return null;
        }

        return key;
    }

    // Read up to `cap` bytes from the stream. If a (cap+1)th byte exists, set exceeded=true
    // and return whatever was read (caller discards it via the error path). Used for files
    // where FileInfo.Length is unreliable (/dev/zero, /proc/kcore — both report length 0
    // but stream forever). Reads in 64 KB chunks.
    private static byte[] ReadCapped(Stream stream, int cap, out bool exceeded)
    {
        using (stream)
        {
            using var ms = new MemoryStream();
            byte[] buffer = new byte[64 * 1024];
            int total = 0;
            while (true)
            {
                int toRead = Math.Min(buffer.Length, cap + 1 - total);
                if (toRead <= 0) break;
                int n = stream.Read(buffer, 0, toRead);
                if (n == 0) break;
                ms.Write(buffer, 0, n);
                total += n;
                if (total > cap)
                {
                    exceeded = true;
                    return ms.ToArray();
                }
            }
            exceeded = false;
            return ms.ToArray();
        }
    }

    // Read up to `cap` bytes' worth of chars from the TextReader. We bound by char count
    // (strictly tighter than byte count for non-ASCII) and check the encoded byte length.
    private static byte[] ReadCappedStdin(TextReader stdin, int cap, out bool exceeded)
    {
        char[] buffer = new char[64 * 1024];
        var sb = new StringBuilder();
        int total = 0;
        while (true)
        {
            int toRead = Math.Min(buffer.Length, cap + 1 - total);
            if (toRead <= 0) break;
            int n = stdin.Read(buffer, 0, toRead);
            if (n == 0) break;
            sb.Append(buffer, 0, n);
            total += n;
            if (total > cap)
            {
                exceeded = true;
                return Encoding.UTF8.GetBytes(sb.ToString());
            }
        }
        byte[] encoded = Encoding.UTF8.GetBytes(sb.ToString());
        if (encoded.Length > cap)
        {
            exceeded = true;
            return encoded;
        }
        exceeded = false;
        return encoded;
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
