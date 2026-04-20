#nullable enable
using System;
using System.IO;

namespace Winix.Protect;

/// <summary>Reads and writes the 6-byte <c>protect</c> file header: magic | version | platform marker.</summary>
public static class Header
{
    private static readonly byte[] Magic = [(byte)'W', (byte)'P', (byte)'R', (byte)'T'];
    private const byte CurrentVersion = 0x01;

    /// <summary>Output of <see cref="Read"/>.</summary>
    public readonly record struct ReadResult(byte Version, PlatformMarker Marker);

    /// <summary>The full header length in bytes.</summary>
    public const int Length = 6;

    /// <summary>Write a v1 header with the given platform marker.</summary>
    public static void Write(Stream stream, PlatformMarker marker)
    {
        stream.Write(Magic, 0, Magic.Length);
        stream.WriteByte(CurrentVersion);
        stream.WriteByte((byte)marker);
    }

    /// <summary>Read and validate the header.</summary>
    /// <exception cref="FormatException">Magic, version, or marker is invalid.</exception>
    /// <exception cref="EndOfStreamException">Stream is shorter than <see cref="Length"/> bytes.</exception>
    public static ReadResult Read(Stream stream)
    {
        byte[] buffer = new byte[Length];
        int read = 0;
        while (read < Length)
        {
            int n = stream.Read(buffer, read, Length - read);
            if (n == 0)
            {
                throw new EndOfStreamException($"Expected {Length} header bytes; got {read}.");
            }
            read += n;
        }

        for (int i = 0; i < Magic.Length; i++)
        {
            if (buffer[i] != Magic[i])
            {
                throw new FormatException("Bad magic — not a protect file.");
            }
        }

        byte version = buffer[4];
        if (version != CurrentVersion)
        {
            throw new FormatException($"Unsupported version: 0x{version:X2}. This build understands version 0x{CurrentVersion:X2}.");
        }

        byte markerByte = buffer[5];
        if (!IsKnownMarker(markerByte))
        {
            throw new FormatException($"Unknown platform marker: 0x{markerByte:X2}.");
        }

        return new ReadResult(version, (PlatformMarker)markerByte);
    }

    private static bool IsKnownMarker(byte b)
    {
        return b == (byte)PlatformMarker.WindowsDpapiUser
            || b == (byte)PlatformMarker.WindowsDpapiMachine
            || b == (byte)PlatformMarker.MacKeychainUser
            || b == (byte)PlatformMarker.MacKeychainMachine
            || b == (byte)PlatformMarker.LinuxLibsecretUser;
    }
}
