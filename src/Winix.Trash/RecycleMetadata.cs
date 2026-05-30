#nullable enable
using System;
using System.Buffers.Binary;
using System.Text;

namespace Winix.Trash;

/// <summary>One parsed Windows Recycle Bin <c>$I</c> metadata record.</summary>
public sealed record RecycleEntry(string OriginalPath, long SizeBytes, DateTime DeletedUtc);

/// <summary>Parses Windows Recycle Bin <c>$I</c> metadata files (format version 2, Win10+).
/// Pure byte-level parsing — no Shell COM — so it is AOT-clean and unit-testable on any OS.</summary>
public static class RecycleMetadata
{
    /// <summary>Parses one <c>$I</c> file's bytes, returning null if the buffer is too short, not
    /// format version 2 (v1/pre-Win10 entries are skipped — F17), the path length is invalid, or the
    /// deletion FILETIME is out of range (F8). Returning null rather than throwing lets <c>List()</c>
    /// skip a single corrupt entry without aborting the whole enumeration (F9).</summary>
    public static RecycleEntry? TryParseIFile(byte[] bytes)
    {
        if (bytes.Length < 28) { return null; }
        long header = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(0, 8));
        if (header != 2) { return null; }   // F17: only Win10+ v2 parsed; older entries skipped
        long size = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(8, 8));
        long filetime = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(16, 8));
        int charCount = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(24, 4));
        int pathByteLen = (charCount - 1) * 2; // exclude null terminator
        if (charCount < 1 || pathByteLen < 0 || 28 + pathByteLen > bytes.Length) { return null; }
        string path = Encoding.Unicode.GetString(bytes, 28, pathByteLen);
        DateTime deleted;
        try { deleted = DateTime.FromFileTimeUtc(filetime); }
        catch (ArgumentOutOfRangeException) { return null; }   // F8: garbage/negative FILETIME
        return new RecycleEntry(path, size, deleted);
    }
}
