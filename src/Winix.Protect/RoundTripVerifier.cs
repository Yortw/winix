#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;

namespace Winix.Protect;

/// <summary>
/// Verifies that an encrypted stream decrypts to plaintext whose SHA-256 matches a supplied
/// expected source hash. Used by the in-place executor to detect corruption before it overwrites
/// the original file with its encrypted replacement.
/// </summary>
public static class RoundTripVerifier
{
    /// <summary>
    /// Decrypts <paramref name="encryptedStream"/> chunk-by-chunk, streaming plaintext through a
    /// SHA-256 hasher, and throws if the resulting digest does not match <paramref name="expectedSourceHash"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Header marker does not match the backend, or the round-trip hash does not match.
    /// </exception>
    public static void Verify(Stream encryptedStream, IProtectBackend backend, byte[] expectedSourceHash)
    {
        Header.ReadResult hdr = Header.Read(encryptedStream);
        if (hdr.Marker != backend.Marker)
        {
            throw new InvalidOperationException("Round-trip verification: header platform-marker mismatch.");
        }

        byte[] headerBytes = [(byte)'W', (byte)'P', (byte)'R', (byte)'T', hdr.Version, (byte)hdr.Marker];
        using IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using HashingStream sink = new(hasher);
        ChunkReader.Read(encryptedStream, sink, backend, headerBytes);

        byte[] actual = hasher.GetCurrentHash();
        if (!CryptographicOperations.FixedTimeEquals(actual, expectedSourceHash))
        {
            throw new InvalidOperationException(
                "Encryption integrity check failed — round-trip SHA-256 mismatch. Source file preserved. This is a bug; please report.");
        }
    }

    private sealed class HashingStream : Stream
    {
        private readonly IncrementalHash _hasher;
        public HashingStream(IncrementalHash hasher) { _hasher = hasher; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => _hasher.AppendData(buffer, offset, count);
    }
}
