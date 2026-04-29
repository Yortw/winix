#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;

namespace Winix.Protect;

/// <summary>Reads and writes the 22-byte <c>protect</c> file header: magic | version | platform marker | file-id.</summary>
public static class Header
{
    private static readonly byte[] Magic = [(byte)'W', (byte)'P', (byte)'R', (byte)'T'];
    private const byte CurrentVersion = 0x01;

    /// <summary>Length of the FileId in bytes.</summary>
    public const int FileIdLength = 16;

    /// <summary>Byte offset of the FileId within the serialized header.</summary>
    public const int FileIdOffset = 4 + 1 + 1; // magic(4) + version(1) + marker(1)

    /// <summary>The full header length in bytes.</summary>
    public const int Length = FileIdOffset + FileIdLength;

    /// <summary>Output of <see cref="Read"/>. <see cref="FileId"/> is the 16-byte per-file binding token.</summary>
    public readonly record struct ReadResult(byte Version, PlatformMarker Marker, byte[] FileId);

    /// <summary>Generate a fresh random <see cref="FileIdLength"/>-byte file id.</summary>
    public static byte[] NewFileId()
    {
        byte[] id = new byte[FileIdLength];
        RandomNumberGenerator.Fill(id);
        return id;
    }

    /// <summary>
    /// Write a v1 header with the given platform marker and FileId.
    /// </summary>
    /// <param name="fileId">Must be exactly <see cref="FileIdLength"/> bytes.</param>
    /// <exception cref="ArgumentNullException"><paramref name="fileId"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="fileId"/> is not <see cref="FileIdLength"/> bytes.</exception>
    public static void Write(Stream stream, PlatformMarker marker, byte[] fileId)
    {
        if (fileId is null) { throw new ArgumentNullException(nameof(fileId)); }
        if (fileId.Length != FileIdLength)
        {
            throw new ArgumentException($"FileId must be {FileIdLength} bytes (got {fileId.Length}).", nameof(fileId));
        }
        stream.Write(Magic, 0, Magic.Length);
        stream.WriteByte(CurrentVersion);
        stream.WriteByte((byte)marker);
        stream.Write(fileId, 0, FileIdLength);
    }

    /// <summary>Read and validate the header. Returns the parsed marker and FileId.</summary>
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

        byte[] fileId = new byte[FileIdLength];
        Array.Copy(buffer, FileIdOffset, fileId, 0, FileIdLength);
        return new ReadResult(version, (PlatformMarker)markerByte, fileId);
    }

    /// <summary>
    /// Build the canonical "header bytes" used as AAD input on the AEAD path. Wraps the literal
    /// byte composition so callers cannot drift from the on-wire format.
    /// </summary>
    /// <param name="fileId">Must be exactly <see cref="FileIdLength"/> bytes.</param>
    /// <exception cref="ArgumentNullException"><paramref name="fileId"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="fileId"/> is not <see cref="FileIdLength"/> bytes.</exception>
    public static byte[] SerializeForAad(PlatformMarker marker, byte[] fileId)
    {
        if (fileId is null) { throw new ArgumentNullException(nameof(fileId)); }
        if (fileId.Length != FileIdLength)
        {
            throw new ArgumentException($"FileId must be {FileIdLength} bytes (got {fileId.Length}).", nameof(fileId));
        }
        byte[] result = new byte[Length];
        result[0] = (byte)'W';
        result[1] = (byte)'P';
        result[2] = (byte)'R';
        result[3] = (byte)'T';
        result[4] = CurrentVersion;
        result[5] = (byte)marker;
        Array.Copy(fileId, 0, result, FileIdOffset, FileIdLength);
        return result;
    }

    /// <summary>Copy the FileId out of a serialized header. Caller must pass exactly <see cref="Length"/> bytes.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="headerBytes"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="headerBytes"/> is not <see cref="Length"/> bytes.</exception>
    public static byte[] ExtractFileId(byte[] headerBytes)
    {
        if (headerBytes is null) { throw new ArgumentNullException(nameof(headerBytes)); }
        if (headerBytes.Length != Length)
        {
            throw new ArgumentException($"headerBytes must be {Length} bytes (got {headerBytes.Length}).", nameof(headerBytes));
        }
        byte[] fileId = new byte[FileIdLength];
        Array.Copy(headerBytes, FileIdOffset, fileId, 0, FileIdLength);
        return fileId;
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
