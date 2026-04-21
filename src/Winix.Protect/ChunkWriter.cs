#nullable enable
using System;
using System.IO;

namespace Winix.Protect;

/// <summary>
/// Encrypts a plaintext stream into a sequence of AEAD-protected (or DPAPI-protected) chunks,
/// emitting the file header first. The final chunk is flagged via <see cref="AadContext.IsFinal"/>
/// and its length may be shorter than <see cref="DefaultChunkSize"/>. Chunks are encrypted via
/// <see cref="IProtectBackend.EncryptChunk"/> so the writer is backend-agnostic.
/// </summary>
public static class ChunkWriter
{
    /// <summary>Default plaintext chunk size (64 KiB). Final chunk may be smaller.</summary>
    public const int DefaultChunkSize = 64 * 1024;

    /// <summary>
    /// Read <paramref name="source"/> to end and write the full ciphertext
    /// (header followed by encrypted chunks) to <paramref name="destination"/>.
    /// Exactly one chunk will be flagged <c>isFinal=true</c>; if the source is empty, that final
    /// chunk is zero-length plaintext, which still produces a tagged AEAD record so truncation
    /// before the final chunk is detectable.
    /// </summary>
    public static void Write(Stream source, Stream destination, IProtectBackend backend, byte[] headerBytes, int chunkSize = DefaultChunkSize)
    {
        if (chunkSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be positive.");
        }

        destination.Write(headerBytes, 0, headerBytes.Length);

        byte[] buffer = new byte[chunkSize];
        long chunkIndex = 0;
        int buffered = 0;
        bool eofSeen = false;

        while (true)
        {
            int n = source.Read(buffer, buffered, chunkSize - buffered);
            if (n == 0)
            {
                eofSeen = true;
            }
            else
            {
                buffered += n;
            }

            if (buffered == chunkSize && !eofSeen)
            {
                // Peek one byte to decide whether the just-filled buffer is truly the final chunk.
                // Without the peek we'd emit the final-size chunk as non-final, then face an empty
                // EOF read next iteration — which would force us to emit a zero-byte final chunk.
                // That would work but wastes a chunk; this keeps the common "exact-multiple" case tidy.
                byte[] oneByte = new byte[1];
                int peek = source.Read(oneByte, 0, 1);
                if (peek == 0)
                {
                    byte[] chunk = new byte[chunkSize];
                    Array.Copy(buffer, chunk, chunkSize);
                    AadContext aad = new(headerBytes, chunkIndex, true);
                    byte[] encrypted = backend.EncryptChunk(chunk, aad, true);
                    WriteEncryptedChunk(destination, encrypted, backend.Marker);
                    return;
                }
                byte[] nonFinal = new byte[chunkSize];
                Array.Copy(buffer, nonFinal, chunkSize);
                AadContext nonFinalAad = new(headerBytes, chunkIndex++, false);
                byte[] encNonFinal = backend.EncryptChunk(nonFinal, nonFinalAad, false);
                WriteEncryptedChunk(destination, encNonFinal, backend.Marker);
                buffer[0] = oneByte[0];
                buffered = 1;
            }
            else if (eofSeen)
            {
                byte[] finalChunk = new byte[buffered];
                Array.Copy(buffer, finalChunk, buffered);
                AadContext aad = new(headerBytes, chunkIndex, true);
                byte[] encrypted = backend.EncryptChunk(finalChunk, aad, true);
                WriteEncryptedChunk(destination, encrypted, backend.Marker);
                return;
            }
        }
    }

    private static bool IsAeadMarker(PlatformMarker marker)
        => marker == PlatformMarker.MacKeychainUser
        || marker == PlatformMarker.MacKeychainMachine
        || marker == PlatformMarker.LinuxLibsecretUser;

    // AEAD backends emit chunks that already carry a 4-byte length field inside the 17-byte prefix,
    // so the reader can self-delimit. DPAPI blobs have no such internal framing, so we must prepend
    // a big-endian 4-byte length on the wire to match ChunkReader's expectations for DPAPI markers.
    private static void WriteEncryptedChunk(Stream destination, byte[] encrypted, PlatformMarker marker)
    {
        if (!IsAeadMarker(marker))
        {
            int length = encrypted.Length;
            byte[] lengthBytes =
            [
                (byte)((length >> 24) & 0xFF),
                (byte)((length >> 16) & 0xFF),
                (byte)((length >> 8) & 0xFF),
                (byte)(length & 0xFF),
            ];
            destination.Write(lengthBytes, 0, 4);
        }
        destination.Write(encrypted, 0, encrypted.Length);
    }
}
