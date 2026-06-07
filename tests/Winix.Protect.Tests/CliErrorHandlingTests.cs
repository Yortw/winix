#nullable enable
using System;
using System.IO;
using Winix.SecretStore;
using Xunit;
using Winix.Protect;

namespace Winix.Protect.Tests;

[Collection(SharedKeystoreCollection.Name)]
public class CliErrorHandlingTests
{
    [Fact]
    public void Unprotect_TruncatedFile_ReturnsRuntimeError()
    {
        string path = Path.Combine(Path.GetTempPath(), $"winix-trunc-{Guid.NewGuid():N}.prot");
        File.WriteAllBytes(path, [(byte)'W', (byte)'P']);
        try
        {
            int exit = Winix.Protect.Cli.Run([path], "unprotect");
            Assert.Equal(126, exit);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void Unprotect_TruncatedFile_StderrIsReadableNoResourceKey()
    {
        // The 2-byte file is shorter than the 22-byte header, so Header.Read throws
        // EndOfStreamException, hitting Cli.Run's EndOfStreamException catch. Under
        // UseSystemResourceKeys=true (mirrored in this test csproj) a leaked framework .Message
        // would surface a bare CoreLib resource key; assert the prefix-only message is emitted.
        string path = Path.Combine(Path.GetTempPath(), $"winix-trunc-msg-{Guid.NewGuid():N}.prot");
        File.WriteAllBytes(path, [(byte)'W', (byte)'P']);
        StringWriter capturedErr = new();
        TextWriter originalErr = Console.Error;
        Console.SetError(capturedErr);
        try
        {
            int exit = Winix.Protect.Cli.Run([path], "unprotect");
            Assert.Equal(126, exit);
            string err = capturedErr.ToString();
            Assert.Contains("truncated or not a protect file", err, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("EndOfStreamException", err, StringComparison.Ordinal);
            Assert.DoesNotContain("IO_", err, StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(originalErr);
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void Unprotect_BadMagic_ReturnsRuntimeError()
    {
        string path = Path.Combine(Path.GetTempPath(), $"winix-badmagic-{Guid.NewGuid():N}.prot");
        // 22 bytes of zeros — header.Read consumes them, then fails the magic check (FormatException),
        // which is mapped to a runtime error by Cli.Run's existing FormatException handler.
        File.WriteAllBytes(path, new byte[22]);
        try
        {
            int exit = Winix.Protect.Cli.Run([path], "unprotect");
            Assert.Equal(126, exit);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void Protect_BackendRaisesSecretStoreException_SurfacesVerbatimNotTypeName()
    {
        // The SecretStoreException catch arm (Cli.Run) was previously verified by inspection only —
        // the real keychain backends raise it on environmental failures (locked collection, missing
        // secret-tool) that can't be triggered on demand. BackendFactory.CreateOverride is the
        // deterministic seam. The backend message is project-authored English and must surface
        // VERBATIM, never degraded to the type name by the broad catch's SafeError routing.
        string dir = Path.Combine(Path.GetTempPath(), $"winix-ssex-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        BackendFactory.CreateOverride = _ =>
            throw new SecretStoreException("secret-tool store failed (exit 1): collection locked");
        StringWriter capturedErr = new();
        TextWriter originalErr = Console.Error;
        Console.SetError(capturedErr);
        try
        {
            string input = Path.Combine(dir, "secret.txt");
            File.WriteAllText(input, "data");
            int exit = Winix.Protect.Cli.Run([input], "protect");
            Assert.Equal(126, exit);
            string err = capturedErr.ToString();
            Assert.Contains("secret-tool store failed (exit 1): collection locked", err, StringComparison.Ordinal);
            Assert.DoesNotContain("SecretStoreException", err, StringComparison.Ordinal);
        }
        finally
        {
            BackendFactory.CreateOverride = null;
            Console.SetError(originalErr);
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Protect_BackendRaisesUnmappedFrameworkException_RoutesThroughSafeErrorNoResourceKey()
    {
        // Invariant negative for the seam: an exception type no typed arm matches must fall to the
        // broad catch and route through SafeError (type-name text, no raw framework .Message which
        // is an SR key under UseSystemResourceKeys — mirrored on this test csproj).
        string dir = Path.Combine(Path.GetTempPath(), $"winix-ssex-neg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        BackendFactory.CreateOverride = _ => throw new NotSupportedException();
        StringWriter capturedErr = new();
        TextWriter originalErr = Console.Error;
        Console.SetError(capturedErr);
        try
        {
            string input = Path.Combine(dir, "secret.txt");
            File.WriteAllText(input, "data");
            int exit = Winix.Protect.Cli.Run([input], "protect");
            Assert.Equal(126, exit);
            string err = capturedErr.ToString();
            Assert.Contains("unexpected error", err, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("NotSupported_", err, StringComparison.Ordinal);
        }
        finally
        {
            BackendFactory.CreateOverride = null;
            Console.SetError(originalErr);
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Protect_OutputParentDirMissing_StderrIsReadableNoResourceKey()
    {
        // CR-3: writing to an -o path under a non-existent directory throws DirectoryNotFoundException
        // at FileStream CreateNew. Its .Message is the bare SR key "IO_PathNotFound_Path" under
        // UseSystemResourceKeys (mirrored on this test csproj). Cli must emit our own friendly text
        // naming the path, never the leaked key.
        string dir = Path.Combine(Path.GetTempPath(), $"winix-nodir-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string input = Path.Combine(dir, "secret.txt");
            File.WriteAllText(input, "round-trip me");
            string missingOut = Path.Combine(dir, "does", "not", "exist", "out.prot");

            StringWriter capturedErr = new();
            TextWriter originalErr = Console.Error;
            Console.SetError(capturedErr);
            try
            {
                int exit = Winix.Protect.Cli.Run([input, "-o", missingOut], "protect");
                Assert.Equal(126, exit);
                string err = capturedErr.ToString();
                Assert.Contains("no such directory", err, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("IO_PathNotFound", err, StringComparison.Ordinal);
                Assert.DoesNotContain("DirectoryNotFoundException", err, StringComparison.Ordinal);
            }
            finally { Console.SetError(originalErr); }
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    [SkippableFact]
    public void Protect_SourceFileLocked_StderrIsReadableNoResourceKey()
    {
        // CR-1: a sharing-violation IOException (source file held open exclusively elsewhere) is caught
        // by Cli.Run's broad IOException arm. Its .Message is the bare SR key "IO_SharingViolation_File"
        // under UseSystemResourceKeys (mirrored on this test csproj). Cli must route it through
        // SafeError, never the leaked key. Windows-only: POSIX advisory locking does not block the
        // FileShare.Read open the same way, so the sharing-violation path is Windows-specific.
        Skip.IfNot(OperatingSystem.IsWindows(), "FileShare.None contention is Windows sharing-violation semantics.");
        if (!OperatingSystem.IsWindows())
        {
            return; // Keep CA1416 happy after the Skip.IfNot above.
        }

        string dir = Path.Combine(Path.GetTempPath(), $"winix-locked-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        // Adversarial F2: hold the lock across Cli.Run via an explicit finally-disposed handle, NOT an
        // early-closing using block that would release before Run opens the source.
        FileStream? lockHandle = null;
        StringWriter capturedErr = new();
        TextWriter originalErr = Console.Error;
        Console.SetError(capturedErr);
        try
        {
            string input = Path.Combine(dir, "secret.txt");
            File.WriteAllText(input, "data");
            // Cli.Run opens the SOURCE (FileShare.Read) before any destination open; holding the source
            // with FileShare.None makes that open throw a sharing violation, hitting the broad arm.
            lockHandle = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.None);

            int exit = Winix.Protect.Cli.Run([input], "protect");

            Assert.Equal(126, exit);
            string err = capturedErr.ToString();
            Assert.DoesNotContain("IO_", err, StringComparison.Ordinal);
            Assert.Contains("I/O error accessing", err, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("secret.txt", err, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Console.SetError(originalErr);
            lockHandle?.Dispose();
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [SkippableFact]
    public void Unprotect_TamperedCiphertext_StderrSaysAuthenticationFailed()
    {
        // Closes adversarial F6: AuthenticationTagMismatchException (file tampered) gets a different
        // user-facing message than generic CryptographicException (different user/machine).
        // Run on platforms where the AEAD path is exercised end-to-end (mac/linux). Windows uses
        // DPAPI which surfaces tampering through its own envelope error path with different wording.
        Skip.IfNot(!OperatingSystem.IsWindows(), "AEAD path; Windows uses DPAPI envelope error path.");
        if (OperatingSystem.IsWindows())
        {
            return; // Keep CA1416 happy after the Skip.IfNot above.
        }

        string dir = Path.Combine(Path.GetTempPath(), $"winix-tamper-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string input = Path.Combine(dir, "secrets.json");
            string output = Path.Combine(dir, "secrets.json.prot");
            File.WriteAllText(input, "round-trip me");

            // Capture stderr around the encrypt call too. The test originally only captured
            // stderr around unprotect (the assertion target), so an intermittent macOS CI
            // failure of the protect step (exit 126 — InvalidOperationException from the
            // Keychain backend) shows up only as "Expected: 0, Actual: 126" with no diagnostic.
            // When this assertion fails, the captured stderr should reveal whether it's a
            // Keychain reachability issue, an "existing key wrong size" anomaly, or something
            // else — and we can then fix the root cause rather than guessing at gates.
            StringWriter encErr = new();
            TextWriter originalErr = Console.Error;
            Console.SetError(encErr);
            int encExit;
            try
            {
                encExit = Winix.Protect.Cli.Run([input, "-o", output], "protect");
            }
            finally { Console.SetError(originalErr); }
            Assert.True(encExit == 0,
                $"protect step expected exit 0, got {encExit}. stderr: <{encErr.ToString().Trim()}>");

            // Remove the plaintext source so unprotect's default output path (`output` minus
            // the `.prot` suffix == `input`) doesn't collide with an existing file. Without
            // this, Task 7's overwrite-refusal causes unprotect to return 125 (usage error)
            // before the AEAD path ever runs, and we never observe the auth-tag-mismatch
            // message this test exists to assert.
            File.Delete(input);

            // Flip a byte in the ciphertext body (after the 22-byte header).
            byte[] bytes = File.ReadAllBytes(output);
            bytes[Header.Length + 30] ^= 0x01;
            File.WriteAllBytes(output, bytes);

            StringWriter capturedErr = new();
            TextWriter originalErr2 = Console.Error;
            Console.SetError(capturedErr);
            try
            {
                int decExit = Winix.Protect.Cli.Run([output], "unprotect");
                Assert.Equal(126, decExit);
                string err = capturedErr.ToString();
                Assert.Contains("authentication", err, StringComparison.OrdinalIgnoreCase);
            }
            finally { Console.SetError(originalErr2); }
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }
}
