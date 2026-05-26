#nullable enable
using System;
using Xunit;
using Winix.QrCode;
using Winix.QrCode.Renderers;

namespace Winix.QrCode.Tests;

public class PngRendererTests
{
    // PNG magic: 89 50 4E 47 0D 0A 1A 0A
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [Fact]
    public void Render_ReturnsNonEmptyBytes()
    {
        byte[] png = PngRenderer.Render("hello", EccLevel.M, pixelsPerModule: 4, drawQuietZone: true);
        Assert.NotEmpty(png);
    }

    [Fact]
    public void Render_StartsWithPngMagicBytes()
    {
        byte[] png = PngRenderer.Render("hello", EccLevel.M, pixelsPerModule: 4, drawQuietZone: true);
        for (int i = 0; i < PngMagic.Length; i++)
        {
            Assert.Equal(PngMagic[i], png[i]);
        }
    }

    [Fact]
    public void Render_IhdrWidthHeight_ReflectQuietZoneAndScale()
    {
        // IHDR chunk: bytes 8-11 are chunk length (= 13), 12-15 are "IHDR",
        // 16-19 are width (big-endian), 20-23 are height (big-endian).
        byte[] png = PngRenderer.Render("a", EccLevel.L, pixelsPerModule: 10, drawQuietZone: true);

        int width  = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        int height = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];

        Assert.Equal(width, height);                     // QR is square
        Assert.True(width > 0);
        Assert.Equal(0, width % 10);                     // multiple of pixelsPerModule
    }

    [Fact]
    public void Render_NoQuietZone_SmallerOutput()
    {
        byte[] withZone = PngRenderer.Render("hello", EccLevel.M, pixelsPerModule: 10, drawQuietZone: true);
        byte[] noZone   = PngRenderer.Render("hello", EccLevel.M, pixelsPerModule: 10, drawQuietZone: false);

        int wWithZone  = (withZone[16] << 24) | (withZone[17] << 16) | (withZone[18] << 8) | withZone[19];
        int wNoZone    = (noZone[16]   << 24) | (noZone[17]   << 16) | (noZone[18]   << 8) | noZone[19];

        // Quiet zone adds 2*4*pixelsPerModule = 80 px to each dimension.
        Assert.Equal(80, wWithZone - wNoZone);
    }

    [Fact]
    public void Render_DifferentEccLevels_AllProduceValidPng()
    {
        foreach (EccLevel ecc in new[] { EccLevel.L, EccLevel.M, EccLevel.Q, EccLevel.H })
        {
            byte[] png = PngRenderer.Render("test", ecc, pixelsPerModule: 4, drawQuietZone: true);
            byte[] prefix = new byte[8];
            Array.Copy(png, 0, prefix, 0, 8);
            Assert.Equal(PngMagic, prefix);
        }
    }
}
