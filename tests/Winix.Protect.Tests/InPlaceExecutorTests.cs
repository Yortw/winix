#nullable enable
using System;
using System.IO;
using System.Linq;
using Xunit;
using Winix.Protect;
using Winix.SecretStore;

namespace Winix.Protect.Tests;

public class InPlaceExecutorTests
{
    private sealed class TestAeadBackend : AeadBackend
    {
        public TestAeadBackend(ISecretStore store) : base(store, PlatformMarker.MacKeychainUser, "test-ns", "test-key") { }
    }

    private static string MakeTempFile(string contents)
    {
        string path = Path.Combine(Path.GetTempPath(), $"winix-protect-test-{Guid.NewGuid():N}");
        File.WriteAllText(path, contents);
        return path;
    }

    [Fact]
    public void Encrypt_InPlace_ReplacesFile()
    {
        string path = MakeTempFile("hello world");
        try
        {
            TestAeadBackend backend = new(new NullSecretStore());
            InPlaceExecutor.ExecuteEncrypt(path, backend, verify: true);
            byte[] onDisk = File.ReadAllBytes(path);
            Assert.Equal((byte)'W', onDisk[0]);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Encrypt_InPlace_LeavesNoTempFile()
    {
        string path = MakeTempFile("hello");
        try
        {
            TestAeadBackend backend = new(new NullSecretStore());
            InPlaceExecutor.ExecuteEncrypt(path, backend, verify: true);
            string leftover = Directory.GetFiles(Path.GetDirectoryName(path)!, "*.winix-tmp.*")
                .FirstOrDefault(f => Path.GetFileName(f).StartsWith(Path.GetFileName(path)))
                ?? string.Empty;
            Assert.Equal(string.Empty, leftover);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
