#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Winix.Digest;

/// <summary>One hash result — raw bytes plus the filename that produced them (null for string/stdin inputs).</summary>
/// <param name="Hash">The hash output as raw bytes. Encoding to hex/base64/base32 is done by the formatting layer.</param>
/// <param name="Path">The path of the file that was hashed, or null if the input was a literal string or stdin.</param>
public readonly record struct HashResult(byte[] Hash, string? Path);

/// <summary>
/// Orchestrates hash computation from an <see cref="InputSource"/>. Produces exactly
/// one <see cref="HashResult"/> for string/stdin/single-file inputs, or one per file
/// for multi-file. Multi-file mode uses all-or-nothing validation — if any path is
/// missing the whole batch fails before any hashing starts, so we don't print hashes
/// for the first N-1 files only to error on file N.
/// </summary>
public static class HashRunner
{
    /// <summary>
    /// Computes hashes for the given input source.
    /// </summary>
    /// <param name="source">The input source: literal string, stdin, a single file, or a file list.</param>
    /// <param name="hasher">The hasher (plain or HMAC) to apply to each input.</param>
    /// <param name="stdin">TextReader used when <paramref name="source"/> is <see cref="StdinInput"/>; tests inject a fake reader.</param>
    /// <param name="error">Out-param: user-facing error message, or null on success.</param>
    /// <returns>On success, one result per input; on failure, an empty list (see <paramref name="error"/>).</returns>
    public static IReadOnlyList<HashResult> Run(
        InputSource source,
        IHasher hasher,
        TextReader stdin,
        out string? error)
    {
        error = null;
        switch (source)
        {
            case StringInput s:
                return HashString(s.Value, hasher);
            case StdinInput:
                return HashStdin(stdin, hasher);
            case SingleFileInput f:
                return HashSingleFile(f.Path, hasher, out error);
            case MultiFileInput m:
                return HashMultiFile(m.Paths, hasher, out error);
            default:
                throw new ArgumentOutOfRangeException(nameof(source), source, null);
        }
    }

    private static IReadOnlyList<HashResult> HashString(string value, IHasher hasher)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        return new[] { new HashResult(hasher.Hash(bytes), null) };
    }

    private static IReadOnlyList<HashResult> HashStdin(TextReader stdin, IHasher hasher)
    {
        // Read stdin as text then re-encode as UTF-8 — matches the string-input path
        // and gives a consistent byte view regardless of the host console encoding.
        string text = stdin.ReadToEnd();
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        return new[] { new HashResult(hasher.Hash(bytes), null) };
    }

    private static IReadOnlyList<HashResult> HashSingleFile(string path, IHasher hasher, out string? error)
    {
        error = null;
        if (!File.Exists(path))
        {
            error = $"'{path}' not found";
            return Array.Empty<HashResult>();
        }
        using var stream = File.OpenRead(path);
        return new[] { new HashResult(hasher.Hash(stream), path) };
    }

    private static IReadOnlyList<HashResult> HashMultiFile(IReadOnlyList<string> paths, IHasher hasher, out string? error)
    {
        error = null;
        foreach (string path in paths)
        {
            if (!File.Exists(path))
            {
                error = $"'{path}' not found";
                return Array.Empty<HashResult>();
            }
        }
        var results = new List<HashResult>(paths.Count);
        foreach (string path in paths)
        {
            using var stream = File.OpenRead(path);
            results.Add(new HashResult(hasher.Hash(stream), path));
        }
        return results;
    }
}
