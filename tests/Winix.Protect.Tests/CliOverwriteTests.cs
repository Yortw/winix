#nullable enable
using System;
using System.IO;
using Xunit;
using Winix.Protect;

namespace Winix.Protect.Tests;

public class CliOverwriteTests
{
    [Fact]
    public void Protect_DefaultRefusesToOverwriteExistingProtFile()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"winix-cli-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string input = Path.Combine(dir, "secrets.json");
            string output = Path.Combine(dir, "secrets.json.prot");
            File.WriteAllText(input, "plaintext");
            File.WriteAllBytes(output, [0xDE, 0xAD, 0xBE, 0xEF]);

            int exit = Winix.Protect.Cli.Run([input], "protect");

            Assert.Equal(125, exit); // UsageError
            byte[] dest = File.ReadAllBytes(output);
            Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, dest);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    [Fact]
    public void Protect_WithForce_OverwritesExistingProtFile()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"winix-cli-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string input = Path.Combine(dir, "secrets.json");
            string output = Path.Combine(dir, "secrets.json.prot");
            File.WriteAllText(input, "plaintext");
            File.WriteAllBytes(output, [0xDE, 0xAD, 0xBE, 0xEF]);

            int exit = Winix.Protect.Cli.Run([input, "--force"], "protect");

            // 0 on Windows/Mac; on Linux without libsecret-tools the keystore lookup may fail with 126.
            // Either way, the overwrite is what we're verifying — assert the file changed.
            byte[] dest = File.ReadAllBytes(output);
            Assert.NotEqual(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, dest);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    [SkippableFact]
    public void Protect_WithForce_DoesNotFollowSymlinkAtDestination()
    {
        Skip.IfNot(!OperatingSystem.IsWindows(), "POSIX symlink semantics; Windows symlinks need admin/dev mode.");

        string dir = Path.Combine(Path.GetTempPath(), $"winix-symlink-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string sensitiveTarget = Path.Combine(dir, "sensitive.txt");
            File.WriteAllText(sensitiveTarget, "DO NOT TOUCH");

            string input = Path.Combine(dir, "secrets.json");
            File.WriteAllText(input, "plaintext");

            string outputPath = Path.Combine(dir, "decoy.prot");
            // Plant a symlink at the destination pointing at the sensitive file.
            File.CreateSymbolicLink(outputPath, sensitiveTarget);

            int exit = Winix.Protect.Cli.Run([input, "-o", outputPath, "--force"], "protect");

            // The sensitive target must NEVER change, regardless of whether the operation succeeded.
            string sensitiveAfter = File.ReadAllText(sensitiveTarget);
            Assert.Equal("DO NOT TOUCH", sensitiveAfter);

            if (exit == 0)
            {
                // On success the symlink at outputPath must have been REPLACED with a real file —
                // not written through. LinkTarget is non-null only for symlinks (.NET 6+).
                FileInfo info = new(outputPath);
                Assert.True(info.Exists, "outputPath should exist after successful protect");
                Assert.Null(info.LinkTarget);
            }
            else
            {
                // On failure (e.g. libsecret unavailable in CI), the symlink should still be intact
                // and the sensitive target untouched (already asserted above).
                FileInfo info = new(outputPath);
                // Either the symlink is still there (Delete-then-CreateNew never reached, or both
                // failed cleanly), OR it was deleted and not recreated. Either is acceptable —
                // the security property is "sensitive target unchanged", which we've already asserted.
                // Just confirm we didn't end up with something WORSE — outputPath is not a regular
                // file with the sensitive target's contents written through it.
                if (info.Exists && info.LinkTarget is null)
                {
                    // It's a regular file. It must NOT contain the sensitive target's bytes.
                    byte[] bytes = File.ReadAllBytes(outputPath);
                    byte[] sensitiveBytes = System.Text.Encoding.UTF8.GetBytes("DO NOT TOUCH");
                    Assert.NotEqual(sensitiveBytes, bytes);
                }
            }
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }
}
