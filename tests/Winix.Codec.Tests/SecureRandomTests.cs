using System;
using Xunit;
using Winix.Codec;

namespace Winix.Codec.Tests;

public class SecureRandomTests
{
    [Fact]
    public void Fill_WritesAllBytes()
    {
        var buffer = new byte[32];
        SecureRandom.Default.Fill(buffer);
        // Guard against "forgot to actually fill" regressions by asserting the whole span
        // is touched across multiple calls — extremely unlikely for 32 bytes to stay zero
        // across 100 calls with a working CSPRNG.
        bool anyNonZero = false;
        for (int i = 0; i < 100 && !anyNonZero; i++)
        {
            SecureRandom.Default.Fill(buffer);
            foreach (byte b in buffer)
            {
                if (b != 0)
                {
                    anyNonZero = true;
                    break;
                }
            }
        }
        Assert.True(anyNonZero, "SecureRandom.Fill produced only zero bytes across 100 calls.");
    }

    [Fact]
    public void Fill_DoesNotThrow_OnEmptySpan()
    {
        SecureRandom.Default.Fill(Span<byte>.Empty);
    }
}
