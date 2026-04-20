#nullable enable
using System;
using System.IO;
using Xunit;
using Winix.Protect;

namespace Winix.Protect.Tests;

public class HeaderTests
{
    [Fact]
    public void Write_EmitsMagicVersionAndMarker()
    {
        using MemoryStream stream = new();
        Header.Write(stream, PlatformMarker.WindowsDpapiUser);
        byte[] bytes = stream.ToArray();
        Assert.Equal(6, bytes.Length);
        Assert.Equal((byte)'W', bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'R', bytes[2]);
        Assert.Equal((byte)'T', bytes[3]);
        Assert.Equal((byte)0x01, bytes[4]);
        Assert.Equal((byte)PlatformMarker.WindowsDpapiUser, bytes[5]);
    }

    [Theory]
    [InlineData(PlatformMarker.WindowsDpapiUser)]
    [InlineData(PlatformMarker.WindowsDpapiMachine)]
    [InlineData(PlatformMarker.MacKeychainUser)]
    [InlineData(PlatformMarker.MacKeychainMachine)]
    [InlineData(PlatformMarker.LinuxLibsecretUser)]
    public void RoundTrip_AllMarkers(PlatformMarker marker)
    {
        using MemoryStream stream = new();
        Header.Write(stream, marker);
        stream.Position = 0;
        Header.ReadResult result = Header.Read(stream);
        Assert.Equal(1, result.Version);
        Assert.Equal(marker, result.Marker);
    }

    [Fact]
    public void Read_BadMagic_Throws()
    {
        using MemoryStream stream = new(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x01, 0x01 });
        FormatException ex = Assert.Throws<FormatException>(() => Header.Read(stream));
        Assert.Contains("magic", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_UnsupportedVersion_Throws()
    {
        using MemoryStream stream = new(new byte[] { (byte)'W', (byte)'P', (byte)'R', (byte)'T', 0xFF, 0x01 });
        FormatException ex = Assert.Throws<FormatException>(() => Header.Read(stream));
        Assert.Contains("version", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_UnknownMarker_Throws()
    {
        using MemoryStream stream = new(new byte[] { (byte)'W', (byte)'P', (byte)'R', (byte)'T', 0x01, 0xFE });
        FormatException ex = Assert.Throws<FormatException>(() => Header.Read(stream));
        Assert.Contains("platform", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_TruncatedHeader_Throws()
    {
        using MemoryStream stream = new(new byte[] { (byte)'W', (byte)'P' });
        Assert.Throws<EndOfStreamException>(() => Header.Read(stream));
    }
}
