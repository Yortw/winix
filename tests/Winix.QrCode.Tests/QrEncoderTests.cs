#nullable enable
using System;
using Xunit;
using Winix.QrCode;

namespace Winix.QrCode.Tests;

public class QrEncoderTests
{
    [Fact]
    public void Encode_SimpleText_ReturnsSquareMatrix()
    {
        QrMatrix m = QrEncoder.Encode("hello", EccLevel.M);
        Assert.Equal(m.Size, m.Modules.GetLength(0));
        Assert.Equal(m.Size, m.Modules.GetLength(1));
        Assert.True(m.Size >= 21, $"Smallest QR (version 1) is 21×21; got {m.Size}");
    }

    [Fact]
    public void Encode_ShortPayload_Version1OrSmall()
    {
        // "abc" at ECC L fits in version 1 (21×21).
        QrMatrix m = QrEncoder.Encode("abc", EccLevel.L);
        Assert.True(m.Size <= 33, $"Expected small version for 'abc' at ECC L; got {m.Size}");
    }

    [Fact]
    public void Encode_HasFinderPatterns()
    {
        // QR finder patterns live at top-left, top-right, bottom-left (7×7 each).
        // Smoke-check the top-left corner: (0,0) is dark, (0,6) is dark.
        QrMatrix m = QrEncoder.Encode("hello", EccLevel.M);
        Assert.True(m.Modules[0, 0], "Top-left finder pattern: (0,0) should be dark.");
        Assert.True(m.Modules[0, 6], "Top-left finder pattern: (0,6) should be dark.");
        Assert.True(m.Modules[6, 0], "Top-left finder pattern: (6,0) should be dark.");
    }

    [Theory]
    [InlineData(EccLevel.L)]
    [InlineData(EccLevel.M)]
    [InlineData(EccLevel.Q)]
    [InlineData(EccLevel.H)]
    public void Encode_AllEccLevels_Succeed(EccLevel ecc)
    {
        QrMatrix m = QrEncoder.Encode("https://example.com", ecc);
        Assert.True(m.Size >= 21);
    }

    [Fact]
    public void Encode_EmptyPayload_Throws()
    {
        Assert.Throws<ArgumentException>(() => QrEncoder.Encode("", EccLevel.M));
    }

    [Fact]
    public void Encode_PayloadExceedingCapacity_ThrowsOverflow()
    {
        // QR Model 1 max at ECC H is ~1,273 bytes. 5,000 alphanumeric chars blows past every version.
        string oversized = new string('A', 5000);
        Assert.Throws<QrCapacityExceededException>(() => QrEncoder.Encode(oversized, EccLevel.H));
    }

    [Fact]
    public void Encode_SameInput_DeterministicMatrix()
    {
        // QRCoder's encoder is deterministic for a given payload + ECC. Lock this in.
        QrMatrix a = QrEncoder.Encode("test-payload", EccLevel.M);
        QrMatrix b = QrEncoder.Encode("test-payload", EccLevel.M);
        Assert.Equal(a.Size, b.Size);
        for (int r = 0; r < a.Size; r++)
        {
            for (int c = 0; c < a.Size; c++)
            {
                Assert.Equal(a.Modules[r, c], b.Modules[r, c]);
            }
        }
    }
}
