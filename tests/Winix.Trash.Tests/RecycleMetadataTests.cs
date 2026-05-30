#nullable enable
using System;
using Winix.Trash;
using Xunit;

namespace Winix.Trash.Tests;

public class RecycleMetadataTests
{
    // Wire-pinned fixture: a hand-laid $I v2 record built per the DOCUMENTED Win10+ format, NOT by
    // re-running the parser's own encoding (which would only verify shape, not wire-correctness — see
    // the suite's protocol-fake testing policy). Layout: int64 LE header(=2) | int64 LE size |
    // int64 LE deletion FILETIME | int32 LE charCount(incl. null) | UTF-16LE path.
    // The FILETIME is the documented Unix-epoch constant 116444736000000000 → 1970-01-01T00:00:00Z,
    // so the expected date is independent of any framework encode call. A wrong field offset or
    // endianness assumption in the parser would decode different values and fail this test.
    [Fact]
    public void Parse_V2_FromLiteralWireFixture()
    {
        byte[] fixture =
        {
            0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // header version = 2
            0xD2, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // size = 1234
            0x00, 0x80, 0x3E, 0xD5, 0xDE, 0xB1, 0x9D, 0x01, // FILETIME = 116444736000000000 (Unix epoch)
            0x05, 0x00, 0x00, 0x00,                         // charCount = 5 ("C:\T" + null)
            0x43, 0x00, 0x3A, 0x00, 0x5C, 0x00, 0x54, 0x00, // "C:\T" UTF-16LE
        };

        RecycleEntry? e = RecycleMetadata.TryParseIFile(fixture);

        Assert.NotNull(e);
        Assert.Equal(@"C:\T", e!.OriginalPath);
        Assert.Equal(1234L, e.SizeBytes);
        Assert.Equal(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc), e.DeletedUtc);
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
