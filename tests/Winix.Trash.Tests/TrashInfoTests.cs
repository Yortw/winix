using System;
using Winix.Trash;
using Xunit;

namespace Winix.Trash.Tests;

public class TrashInfoTests
{
    [Fact]
    public void Write_ProducesSpecFormat_WithPercentEncodedPathAndLocalDate()
    {
        // Path keeps '/' literal; space → %20. Date is local, no timezone suffix.
        string info = TrashInfo.Write("/home/u/My File.txt", new DateTime(2024, 8, 31, 22, 46, 31, DateTimeKind.Local));
        Assert.Equal(
            "[Trash Info]\nPath=/home/u/My%20File.txt\nDeletionDate=2024-08-31T22:46:31\n",
            info);
    }

    [Fact]
    public void Parse_RoundTripsPathAndDate_FromLiteralSpecText()
    {
        // Literal fixture (NOT produced by Write) so a wrong codec on either side is detectable.
        string fixture = "[Trash Info]\nPath=/var/tmp/a%2Bb.bin\nDeletionDate=2023-01-02T03:04:05\n";
        TrashInfoRecord? r = TrashInfo.Parse(fixture);
        Assert.NotNull(r);
        Assert.Equal("/var/tmp/a+b.bin", r.OriginalPath);     // %2B → '+'
        Assert.Equal(new DateTime(2023, 1, 2, 3, 4, 5), r.DeletionLocal);
    }

    [Fact]
    public void Parse_ReturnsNull_OnMissingPathKey()
    {
        Assert.Null(TrashInfo.Parse("[Trash Info]\nDeletionDate=2023-01-02T03:04:05\n"));
    }

    [Theory]   // F12: malformed percent-escapes must never throw (corrupt file must not crash --list)
    [InlineData("[Trash Info]\nPath=/x/%\nDeletionDate=2023-01-02T03:04:05\n")]
    [InlineData("[Trash Info]\nPath=/x/%A\nDeletionDate=2023-01-02T03:04:05\n")]
    [InlineData("[Trash Info]\nPath=/x/%ZZ\nDeletionDate=2023-01-02T03:04:05\n")]
    public void Parse_DoesNotThrow_OnMalformedPercentEscape(string body)
    {
        TrashInfoRecord? r = TrashInfo.Parse(body);   // must not throw
        Assert.NotNull(r);                             // bad escapes pass through literally, not fatal
    }
}
