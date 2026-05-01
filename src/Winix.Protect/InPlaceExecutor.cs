#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;

namespace Winix.Protect;

/// <summary>
/// Orchestrates in-place encrypt/decrypt on a file path via a sibling temp file plus atomic rename.
/// The encrypt path optionally runs a full round-trip SHA-256 verification before swapping the temp
/// over the original, so a corrupted ciphertext cannot silently replace a good plaintext file.
/// </summary>
public static class InPlaceExecutor
{
    /// <summary>
    /// Encrypt <paramref name="targetPath"/> in place. Writes ciphertext to a sibling temp file,
    /// optionally round-trip-verifies it, then atomically renames it over the original. The temp
    /// file is deleted on any failure.
    /// </summary>
    public static void ExecuteEncrypt(string targetPath, IProtectBackend backend, bool verify)
    {
        string targetAbs = Path.GetFullPath(targetPath);
        string directory = Path.GetDirectoryName(targetAbs) ?? ".";
        string tempPath = Path.Combine(directory, $"{Path.GetFileName(targetAbs)}.winix-tmp.{Environment.ProcessId}.{Guid.NewGuid():N}");

        byte[] sourceHash;

        try
        {
            using (FileStream source = new(targetAbs, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                using (FileStream dest = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] fileId = Header.NewFileId();
                    byte[] header = Header.SerializeForAad(backend.Marker, fileId);
                    using TeeReadStream teeSource = new(source, hasher);
                    ChunkWriter.Write(teeSource, dest, backend, header);
                    // FlushFileBuffers / fsync before close so the rename below promotes durable bytes.
                    dest.Flush(flushToDisk: true);
                }
                sourceHash = hasher.GetCurrentHash();
            }

            if (verify)
            {
                using FileStream encrypted = new(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                RoundTripVerifier.Verify(encrypted, backend, sourceHash);
            }

            File.Move(tempPath, targetAbs, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            throw;
        }
    }

    /// <summary>
    /// Decrypt <paramref name="targetPath"/> in place via a sibling temp file plus atomic rename.
    /// The temp file is deleted on any failure.
    /// </summary>
    public static void ExecuteDecrypt(string targetPath, IProtectBackend backend)
    {
        string targetAbs = Path.GetFullPath(targetPath);
        string directory = Path.GetDirectoryName(targetAbs) ?? ".";
        string tempPath = Path.Combine(directory, $"{Path.GetFileName(targetAbs)}.winix-tmp.{Environment.ProcessId}.{Guid.NewGuid():N}");

        try
        {
            using (FileStream source = new(targetAbs, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                using (FileStream dest = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    Header.ReadResult hdr = Header.Read(source);
                    byte[] headerBytes = Header.SerializeForAad(hdr.Marker, hdr.FileId);
                    ChunkReader.Read(source, dest, backend, headerBytes);
                    // FlushFileBuffers / fsync before close so the rename below promotes durable bytes.
                    dest.Flush(flushToDisk: true);
                }
            }
            File.Move(tempPath, targetAbs, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            throw;
        }
    }

    private sealed class TeeReadStream : Stream
    {
        private readonly Stream _underlying;
        private readonly IncrementalHash _hasher;

        public TeeReadStream(Stream underlying, IncrementalHash hasher)
        {
            _underlying = underlying;
            _hasher = hasher;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _underlying.Length;
        public override long Position { get => _underlying.Position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = _underlying.Read(buffer, offset, count);
            if (n > 0)
            {
                _hasher.AppendData(buffer, offset, n);
            }
            return n;
        }
    }
}
