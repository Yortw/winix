#nullable enable
using System;
using System.IO;
using Xunit;
using Winix.Protect;

namespace Winix.Protect.Tests;

public class HeaderTests
{
    [Fact]
    public void Write_EmitsMagicVersionMarkerAndFileId()
    {
        using MemoryStream stream = new();
        byte[] fileId = new byte[16];
        for (int i = 0; i < 16; i++) { fileId[i] = (byte)i; }
        Header.Write(stream, PlatformMarker.WindowsDpapiUser, fileId);
        byte[] bytes = stream.ToArray();
        Assert.Equal(22, bytes.Length);
        Assert.Equal((byte)'W', bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'R', bytes[2]);
        Assert.Equal((byte)'T', bytes[3]);
        Assert.Equal((byte)0x01, bytes[4]);
        Assert.Equal((byte)PlatformMarker.WindowsDpapiUser, bytes[5]);
        for (int i = 0; i < 16; i++) { Assert.Equal((byte)i, bytes[6 + i]); }
    }

    [Fact]
    public void RoundTrip_PreservesFileId()
    {
        byte[] fileId = new byte[16];
        System.Security.Cryptography.RandomNumberGenerator.Fill(fileId);
        using MemoryStream stream = new();
        Header.Write(stream, PlatformMarker.MacKeychainUser, fileId);
        stream.Position = 0;
        Header.ReadResult result = Header.Read(stream);
        Assert.Equal(fileId, result.FileId);
    }

    [Theory]
    [InlineData(PlatformMarker.WindowsDpapiUser)]
    [InlineData(PlatformMarker.WindowsDpapiMachine)]
    [InlineData(PlatformMarker.MacKeychainUser)]
    [InlineData(PlatformMarker.MacKeychainMachine)]
    [InlineData(PlatformMarker.LinuxLibsecretUser)]
    public void RoundTrip_AllMarkers(PlatformMarker marker)
    {
        byte[] fileId = Header.NewFileId();
        using MemoryStream stream = new();
        Header.Write(stream, marker, fileId);
        stream.Position = 0;
        Header.ReadResult result = Header.Read(stream);
        Assert.Equal(1, result.Version);
        Assert.Equal(marker, result.Marker);
        Assert.Equal(fileId, result.FileId);
    }

    [Fact]
    public void Read_BadMagic_Throws()
    {
        // 22 bytes — full header length so magic check fires before EOF.
        byte[] buf = new byte[22];
        using MemoryStream stream = new(buf);
        FormatException ex = Assert.Throws<FormatException>(() => Header.Read(stream));
        Assert.Contains("magic", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_UnsupportedVersion_Throws()
    {
        byte[] buf = new byte[22];
        buf[0] = (byte)'W'; buf[1] = (byte)'P'; buf[2] = (byte)'R'; buf[3] = (byte)'T';
        buf[4] = 0xFF; buf[5] = 0x01;
        using MemoryStream stream = new(buf);
        FormatException ex = Assert.Throws<FormatException>(() => Header.Read(stream));
        Assert.Contains("version", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_UnknownMarker_Throws()
    {
        byte[] buf = new byte[22];
        buf[0] = (byte)'W'; buf[1] = (byte)'P'; buf[2] = (byte)'R'; buf[3] = (byte)'T';
        buf[4] = 0x01; buf[5] = 0xFE;
        using MemoryStream stream = new(buf);
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
