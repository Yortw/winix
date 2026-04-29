#nullable enable
using System;
using System.IO;

namespace Winix.Protect;

/// <summary>
/// Reads a sequence of backend-specific encrypted chunks from a stream (positioned past the header),
/// decrypts each via <see cref="IProtectBackend.DecryptChunk"/>, and writes plaintext to destination.
/// Throws <see cref="FormatException"/> if the stream ends before the final chunk is seen (truncation detection),
/// relying on the AEAD/DPAPI layer to detect in-chunk tampering.
/// </summary>
public static class ChunkReader
{
    /// <summary>
    /// Decrypt <paramref name="source"/> to <paramref name="destination"/> using <paramref name="backend"/>.
    /// The header must already have been consumed (the writer's counterpart <see cref="ChunkWriter.Write"/>
    /// emits the header first, but <see cref="ChunkReader"/> expects the caller to have parsed it so this
    /// method only sees chunk bytes).
    /// </summary>
    public static void Read(Stream source, Stream destination, IProtectBackend backend, byte[] headerBytes)
    {
        if (headerBytes is null) { throw new ArgumentNullException(nameof(headerBytes)); }
        if (headerBytes.Length != Header.Length)
        {
            throw new ArgumentException($"headerBytes must be {Header.Length} bytes (got {headerBytes.Length}).", nameof(headerBytes));
        }

        long chunkIndex = 0;
        while (true)
        {
            byte[] chunkPayload = ReadOneChunk(source, backend.Marker);
            if (chunkPayload.Length == 0)
            {
                throw new FormatException("Ciphertext is truncated (final chunk missing).");
            }

            // The AEAD prefix byte self-describes isFinal, but we still must feed a matching
            // AadContext into Decrypt so the tag verifies. For DPAPI the isFinal flag sits
            // inside the blob so the outer context flag is unused — passing false is safe.
            bool aeadIsFinalGuess = IsAeadMarker(backend.Marker) && chunkPayload[0] == 1;
            AadContext aadForDecrypt = IsAeadMarker(backend.Marker)
                ? new AadContext(headerBytes, chunkIndex, aeadIsFinalGuess)
                : new AadContext(headerBytes, chunkIndex, false);

            (byte[] plaintext, bool isFinal) = backend.DecryptChunk(chunkPayload, aadForDecrypt);
            destination.Write(plaintext, 0, plaintext.Length);

            if (isFinal) return;
            chunkIndex++;
        }
    }

    private static bool IsAeadMarker(PlatformMarker marker)
        => marker == PlatformMarker.MacKeychainUser
        || marker == PlatformMarker.MacKeychainMachine
        || marker == PlatformMarker.LinuxLibsecretUser;

    private static byte[] ReadOneChunk(Stream source, PlatformMarker marker)
    {
        if (IsAeadMarker(marker))
        {
            // AEAD chunk framing: [1 isFinal][12 iv][4 length][length bytes ciphertext][16 tag]
            byte[] prefix = new byte[17];
            int got = ReadExactlyOrPartial(source, prefix);
            if (got == 0) return Array.Empty<byte>();
            if (got < 17) throw new FormatException("Truncated chunk prefix (AEAD).");

            int length = (prefix[13] << 24) | (prefix[14] << 16) | (prefix[15] << 8) | prefix[16];
            byte[] tail = new byte[length + 16];
            int tailGot = ReadExactlyOrPartial(source, tail);
            if (tailGot < tail.Length) throw new FormatException("Truncated chunk body (AEAD).");

            byte[] chunk = new byte[17 + tail.Length];
            Array.Copy(prefix, chunk, 17);
            Array.Copy(tail, 0, chunk, 17, tail.Length);
            return chunk;
        }

        // DPAPI chunk framing: [4 length][length bytes blob].
        byte[] lengthBytes = new byte[4];
        int lenGot = ReadExactlyOrPartial(source, lengthBytes);
        if (lenGot == 0) return Array.Empty<byte>();
        if (lenGot < 4) throw new FormatException("Truncated DPAPI chunk length.");
        int blobLength = (lengthBytes[0] << 24) | (lengthBytes[1] << 16) | (lengthBytes[2] << 8) | lengthBytes[3];

        byte[] blob = new byte[blobLength];
        int blobGot = ReadExactlyOrPartial(source, blob);
        if (blobGot < blobLength) throw new FormatException("Truncated DPAPI chunk blob.");
        return blob;
    }

    private static int ReadExactlyOrPartial(Stream stream, byte[] buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int n = stream.Read(buffer, total, buffer.Length - total);
            if (n == 0) break;
            total += n;
        }
        return total;
    }
}
