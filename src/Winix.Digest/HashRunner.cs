#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Yort.ShellKit;

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
    /// <param name="stdinPayload">Byte stream used when <paramref name="source"/> is <see cref="StdinInput"/>; tests inject a MemoryStream.</param>
    /// <param name="error">Out-param: user-facing error message, or null on success.</param>
    /// <returns>On success, one result per input; on failure, an empty list (see <paramref name="error"/>).</returns>
    /// <remarks>
    /// Round-2 review CR-I3 — the payload stdin path takes a raw byte <see cref="Stream"/> rather
    /// than a <see cref="TextReader"/>. The previous shape read text + re-encoded as UTF-8, which
    /// silently corrupted any non-UTF-8 bytes in binary stdin (`cat binary.bin | digest` produced
    /// a hash that disagreed with `sha256sum binary.bin` for the same file). Byte stream is the
    /// only correct shape for payload hashing.
    /// </remarks>
    public static IReadOnlyList<HashResult> Run(
        InputSource source,
        IHasher hasher,
        Stream stdinPayload,
        out string? error)
    {
        error = null;
        switch (source)
        {
            case StringInput s:
                return HashString(s.Value, hasher);
            case StdinInput:
                return HashStdin(stdinPayload, hasher);
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

    private static IReadOnlyList<HashResult> HashStdin(Stream stdinPayload, IHasher hasher)
    {
        // Stream the raw bytes through the hasher — no text decoding, no UTF-8 round-trip.
        // The IHasher.Hash(Stream) overload handles incremental hashing for both BCL hashers
        // (SHA-2/SHA-3 via HashData) and the BLAKE2b incremental hasher.
        return new[] { new HashResult(hasher.Hash(stdinPayload), null) };
    }

    private static IReadOnlyList<HashResult> HashSingleFile(string path, IHasher hasher, out string? error)
    {
        error = null;
        if (!File.Exists(path))
        {
            error = $"'{path}' not found";
            return Array.Empty<HashResult>();
        }
        // Round-1 review I4 — wrap File.OpenRead in scoped catches so a TOCTOU race
        // between File.Exists (above) and File.OpenRead (here), or a permission change,
        // produces a typed error rather than escaping to Program's outer IOException
        // catch and being silently absorbed as exit 0. Without this, the user sees a
        // clean exit with no hash output and no diagnostic — masquerading as success.
        try
        {
            using var stream = File.OpenRead(path);
            return new[] { new HashResult(hasher.Hash(stream), path) };
        }
        // Round-3 review — single `when` clause across IOException + UnauthorizedAccessException
        // collapses two dead-equivalent catch arms into one, removing the untested branch
        // pointed out by the round-3 test analyzer. Same diagnostic text either way.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = $"failed to read '{path}': {SafeError.Describe(ex)}";
            return Array.Empty<HashResult>();
        }
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
            // Round-1 review I4 — same scoped catch pattern as HashSingleFile.
            // The all-or-nothing contract requires NO partial output if any read fails,
            // so we surface the error on the first failure and return an empty list.
            try
            {
                using var stream = File.OpenRead(path);
                results.Add(new HashResult(hasher.Hash(stream), path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error = $"failed to read '{path}': {SafeError.Describe(ex)}";
                return Array.Empty<HashResult>();
            }
        }
        return results;
    }
}
