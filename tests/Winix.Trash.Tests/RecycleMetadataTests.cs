using System;
using Winix.Trash;
using Xunit;

namespace Winix.Trash.Tests;

public class RecycleMetadataTests
{
    [Fact]
    public void Parse_V2_ReadsSizeDeletionTimeAndPath()
    {
        // ⚠VERIFY against a real $I capture. FILETIME for 2024-01-01T00:00:00Z.
        long filetime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc();
        string path = @"C:\Users\u\note.txt";
        var buf = new System.IO.MemoryStream();
        var w = new System.IO.BinaryWriter(buf);
        w.Write(2L);                       // header version
        w.Write(1234L);                    // original size
        w.Write(filetime);                 // deletion FILETIME
        w.Write(path.Length + 1);          // chars incl. null
        foreach (char c in path) { w.Write((ushort)c); }
        w.Write((ushort)0);                // null terminator
        w.Flush();

        RecycleEntry? e = RecycleMetadata.TryParseIFile(buf.ToArray());
        Assert.NotNull(e);
        Assert.Equal(path, e!.OriginalPath);
        Assert.Equal(1234L, e.SizeBytes);
        Assert.Equal(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), e.DeletedUtc);
    }

    [Fact] // F17: v1/pre-Win10 header → skipped (null), not misparsed as v2
    public void TryParse_ReturnsNull_OnNonV2Header()
    {
        byte[] bytes = new byte[64];
        bytes[0] = 1;   // header version 1
        Assert.Null(RecycleMetadata.TryParseIFile(bytes));
    }

    [Fact] // F8: garbage FILETIME must yield null, not a raw ArgumentOutOfRangeException
    public void TryParse_ReturnsNull_OnOutOfRangeFiletime()
    {
        var buf = new System.IO.MemoryStream();
        var w = new System.IO.BinaryWriter(buf);
        w.Write(2L); w.Write(0L); w.Write(long.MaxValue); w.Write(2); w.Write((ushort)'x'); w.Write((ushort)0);
        w.Flush();
        Assert.Null(RecycleMetadata.TryParseIFile(buf.ToArray()));
    }

    [Fact] // F9: a truncated $I must not throw
    public void TryParse_ReturnsNull_OnShortBuffer()
    {
        Assert.Null(RecycleMetadata.TryParseIFile(new byte[10]));
    }
}
