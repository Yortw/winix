#nullable enable
using System;
using System.IO;
using Xunit;
using Winix.Protect;

namespace Winix.Protect.Tests;

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

            int encExit = Winix.Protect.Cli.Run([input, "-o", output], "protect");
            Assert.Equal(0, encExit);

            // Flip a byte in the ciphertext body (after the 22-byte header).
            byte[] bytes = File.ReadAllBytes(output);
            bytes[Header.Length + 30] ^= 0x01;
            File.WriteAllBytes(output, bytes);

            StringWriter capturedErr = new();
            TextWriter originalErr = Console.Error;
            Console.SetError(capturedErr);
            try
            {
                int decExit = Winix.Protect.Cli.Run([output], "unprotect");
                Assert.Equal(126, decExit);
                string err = capturedErr.ToString();
                Assert.Contains("authentication", err, StringComparison.OrdinalIgnoreCase);
            }
            finally { Console.SetError(originalErr); }
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }
}
