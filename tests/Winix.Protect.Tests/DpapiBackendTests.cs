#nullable enable
using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Xunit;
using Winix.Protect;

namespace Winix.Protect.Tests;

[SupportedOSPlatform("windows")]
public class DpapiBackendTests
{
    private static bool OnWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    [Fact]
    public void EncryptDecrypt_RoundTrips()
    {
        if (!OnWindows) return;
        DpapiBackend backend = new(Scope.User);
        byte[] plaintext = System.Text.Encoding.UTF8.GetBytes("hello world");
        AadContext aad = new([0x57, 0x50, 0x52, 0x54, 0x01, 0x01], 0, true);
        byte[] chunk = backend.EncryptChunk(plaintext, aad, isFinal: true);
        (byte[] decrypted, bool isFinal) = backend.DecryptChunk(chunk, aad);
        Assert.Equal(plaintext, decrypted);
        Assert.True(isFinal);
    }

    [Fact]
    public void Marker_UserScope_IsDpapiUser()
    {
        if (!OnWindows) return;
        Assert.Equal(PlatformMarker.WindowsDpapiUser, new DpapiBackend(Scope.User).Marker);
    }

    [Fact]
    public void Marker_MachineScope_IsDpapiMachine()
    {
        if (!OnWindows) return;
        Assert.Equal(PlatformMarker.WindowsDpapiMachine, new DpapiBackend(Scope.Machine).Marker);
    }

    [Fact]
    public void IsFinal_FlagRoundTrips()
    {
        if (!OnWindows) return;
        DpapiBackend backend = new(Scope.User);
        AadContext aad = new([0x57, 0x50, 0x52, 0x54, 0x01, 0x01], 5, false);
        byte[] chunk = backend.EncryptChunk([1, 2, 3], aad, isFinal: false);
        (_, bool isFinal) = backend.DecryptChunk(chunk, aad);
        Assert.False(isFinal);
    }

    [Fact]
    public void TamperedChunk_ThrowsOnDecrypt()
    {
        if (!OnWindows) return;
        DpapiBackend backend = new(Scope.User);
        AadContext aad = new([0x57, 0x50, 0x52, 0x54, 0x01, 0x01], 0, true);
        byte[] chunk = backend.EncryptChunk([1, 2, 3, 4], aad, isFinal: true);
        chunk[chunk.Length - 1] ^= 0x01;
        Assert.Throws<System.Security.Cryptography.CryptographicException>(
            () => backend.DecryptChunk(chunk, aad));
    }
}
