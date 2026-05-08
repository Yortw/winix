#nullable enable

using System;
using System.Buffers;
using System.IO;

namespace Winix.FileWalk;

/// <summary>
/// Detects whether a file contains text or binary content using the null-byte heuristic.
/// Reads the first 8KB and checks for null bytes — the same method git uses.
/// </summary>
public static class ContentDetector
{
    private const int SampleSize = 8192;

    /// <summary>
    /// Classifies <paramref name="path"/> as text (<see langword="true"/>), binary
    /// (<see langword="false"/>), or unclassifiable (<see langword="null"/>) using the
    /// null-byte heuristic on the first 8KB. Returns <see langword="true"/> for empty
    /// files. Returns <see langword="null"/> when the file could not be read; the
    /// caller can decide whether to skip-with-warning or treat as a default.
    /// </summary>
    /// <remarks>
    /// Round-2 fresh-eyes 2026-05-09 silent-failure-hunter C1 (re-promoted from H1 with
    /// a working reproducer): pre-fix this method returned <see langword="false"/> on
    /// any IOException / UnauthorizedAccessException, masking read failures as "binary."
    /// A common case fires on Windows: a regular text file held by another process with
    /// <c>FileShare.None</c> (MSBuild log, OneDrive cloud placeholder, AV scanner,
    /// editor-open-with-exclusive-lock) returns false, so <c>files . --text</c> silently
    /// drops it and <c>files . --binary</c> silently includes it. Both wrong directions
    /// of the same bug. Now returns null on read failure with the reason in
    /// <paramref name="readError"/>; callers (notably <see cref="FileWalker"/>) record
    /// a <see cref="WalkError"/> and skip the file rather than mis-classify.
    /// </remarks>
    /// <param name="path">Absolute or relative path to the file to classify.</param>
    /// <param name="readError">
    /// On <see langword="null"/> return, a human-readable description of the read failure
    /// (exception type name + message). <see langword="null"/> when classification
    /// succeeded.
    /// </param>
    public static bool? IsTextFile(string path, out string? readError)
    {
        readError = null;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(SampleSize);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            int bytesRead = stream.Read(buffer, 0, SampleSize);

            for (int i = 0; i < bytesRead; i++)
            {
                if (buffer[i] == 0)
                {
                    return false;
                }
            }

            return true;
        }
        catch (IOException ex)
        {
            // SFH I2 round-2 2026-05-09: type name only — ex.Message is locale-dependent
            // under InvariantGlobalization and may leak SR resource keys.
            readError = ex.GetType().Name;
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            readError = ex.GetType().Name;
            return null;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Convenience overload that discards the read-error reason. Returns
    /// <see langword="null"/> on read failure; callers needing diagnostic context should
    /// use the <c>out</c> overload.
    /// </summary>
    /// <param name="path">Absolute or relative path to the file to classify.</param>
    public static bool? IsTextFile(string path) => IsTextFile(path, out _);
}
