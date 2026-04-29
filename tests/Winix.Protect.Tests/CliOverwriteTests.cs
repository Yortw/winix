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

            // Either the operation succeeds (replacing the symlink with a real .prot file)
            // OR it fails — but in NEITHER case should the sensitive target's contents change.
            string sensitiveAfter = File.ReadAllText(sensitiveTarget);
            Assert.Equal("DO NOT TOUCH", sensitiveAfter);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }
}
