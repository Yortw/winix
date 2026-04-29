#nullable enable
using System;
using System.IO;
using Xunit;
using Winix.Protect;

namespace Winix.Protect.Tests;

public class PartialOutputCleanupTests
{
    // Implements IDisposable directly so the test can use `using ThrowingBackend backend = ...`.
    // C# pattern-based dispose only works for ref structs; reference types must explicitly
    // implement IDisposable. Task 12 will add `: IDisposable` to IProtectBackend itself, at
    // which point this redundant interface listing becomes consistent with the rest of the suite.
    private sealed class ThrowingBackend : IProtectBackend, IDisposable
    {
        private int _calls;
        public PlatformMarker Marker => PlatformMarker.MacKeychainUser;
        public byte[] EncryptChunk(byte[] plaintext, AadContext aad, bool isFinal)
        {
            if (++_calls == 2)
            {
                throw new InvalidOperationException("simulated mid-stream failure");
            }
            byte[] chunk = new byte[1 + 12 + 4 + plaintext.Length + 16];
            chunk[0] = isFinal ? (byte)1 : (byte)0;
            // bogus IV / length / tag fields; never read because we throw before the second chunk.
            return chunk;
        }
        public (byte[] plaintext, bool isFinal) DecryptChunk(byte[] chunkPayload, AadContext aad)
            => throw new NotSupportedException();
        public void Dispose() { }
    }

    [Fact]
    public void RunProtectFile_BackendThrowsMidStream_DeletesPartialOutput()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"winix-cleanup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            // 200 KB plaintext forces multiple chunks at the default 64 KB size.
            byte[] payload = new byte[200_000];
            Random.Shared.NextBytes(payload);

            string outputPath = Path.Combine(dir, "x.prot");
            using ThrowingBackend backend = new();

            Assert.Throws<InvalidOperationException>(
                () => Winix.Protect.Cli.RunProtectFile(
                    new MemoryStream(payload),
                    outputPath,
                    backend,
                    noVerify: true,
                    force: false));

            Assert.False(File.Exists(outputPath), "partial output file should have been deleted");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    [Fact]
    public void RunProtectFile_BackendThrowsMidStream_DoesNotTouchInputPath()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"winix-rm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string input = Path.Combine(dir, "secrets.json");
            byte[] payload = new byte[200_000];
            Random.Shared.NextBytes(payload);
            File.WriteAllBytes(input, payload);

            string outputPath = Path.Combine(dir, "x.prot");
            using ThrowingBackend backend = new();

            Assert.Throws<InvalidOperationException>(
                () => Winix.Protect.Cli.RunProtectFile(
                    File.OpenRead(input),
                    outputPath,
                    backend,
                    noVerify: true,
                    force: false));

            Assert.True(File.Exists(input), "input file must still exist after encrypt failure");
            Assert.False(File.Exists(outputPath), "partial output should have been deleted");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }
}
