#nullable enable
using System;
using System.Runtime.Versioning;
using Xunit;
using Winix.Protect;

namespace Winix.Protect.Tests;

[SupportedOSPlatform("windows")]
public class DpapiBackendTests
{
    [SkippableFact]
    public void EncryptDecrypt_RoundTrips()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "DPAPI is Windows-only");
        if (!OperatingSystem.IsWindows()) return; // CA1416 analyzer requires this; deliberate redundancy
        DpapiBackend backend = new(Scope.User);
        byte[] plaintext = System.Text.Encoding.UTF8.GetBytes("hello world");
        AadContext aad = new(Header.SerializeForAad(PlatformMarker.WindowsDpapiUser, new byte[16]), 0, true);
        byte[] chunk = backend.EncryptChunk(plaintext, aad, isFinal: true);
        (byte[] decrypted, bool isFinal) = backend.DecryptChunk(chunk, aad);
        Assert.Equal(plaintext, decrypted);
        Assert.True(isFinal);
    }

    [SkippableFact]
    public void Marker_UserScope_IsDpapiUser()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "DPAPI is Windows-only");
        if (!OperatingSystem.IsWindows()) return; // CA1416 analyzer requires this; deliberate redundancy
        Assert.Equal(PlatformMarker.WindowsDpapiUser, new DpapiBackend(Scope.User).Marker);
    }

    [SkippableFact]
    public void Marker_MachineScope_IsDpapiMachine()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "DPAPI is Windows-only");
        if (!OperatingSystem.IsWindows()) return; // CA1416 analyzer requires this; deliberate redundancy
        Assert.Equal(PlatformMarker.WindowsDpapiMachine, new DpapiBackend(Scope.Machine).Marker);
    }

    [SkippableFact]
    public void IsFinal_FlagRoundTrips()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "DPAPI is Windows-only");
        if (!OperatingSystem.IsWindows()) return; // CA1416 analyzer requires this; deliberate redundancy
        DpapiBackend backend = new(Scope.User);
        AadContext aad = new(Header.SerializeForAad(PlatformMarker.WindowsDpapiUser, new byte[16]), 5, false);
        byte[] chunk = backend.EncryptChunk([1, 2, 3], aad, isFinal: false);
        (_, bool isFinal) = backend.DecryptChunk(chunk, aad);
        Assert.False(isFinal);
    }

    [SkippableFact]
    public void TamperedChunk_ThrowsOnDecrypt()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "DPAPI is Windows-only");
        if (!OperatingSystem.IsWindows()) return; // CA1416 analyzer requires this; deliberate redundancy
        DpapiBackend backend = new(Scope.User);
        AadContext aad = new(Header.SerializeForAad(PlatformMarker.WindowsDpapiUser, new byte[16]), 0, true);
        byte[] chunk = backend.EncryptChunk([1, 2, 3, 4], aad, isFinal: true);
        chunk[chunk.Length - 1] ^= 0x01;
        Assert.Throws<System.Security.Cryptography.CryptographicException>(
            () => backend.DecryptChunk(chunk, aad));
    }

    [SkippableFact]
    public void IntraFileChunkReorder_ThrowsOnDecrypt()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "DPAPI is Windows-only");
        if (!OperatingSystem.IsWindows()) return; // CA1416 analyzer requires this; deliberate redundancy
        DpapiBackend backend = new(Scope.User);

        byte[] fileId = Header.NewFileId();
        byte[] header = Header.SerializeForAad(backend.Marker, fileId);

        AadContext aad0 = new(header, 0, false);
        AadContext aad1 = new(header, 1, true);

        byte[] chunk0 = backend.EncryptChunk([0xAA, 0xBB], aad0, isFinal: false);
        byte[] chunk1 = backend.EncryptChunk([0xCC, 0xDD], aad1, isFinal: true);

        // Try to decrypt chunk1 in the position of chunk0 (chunkIndex=0, isFinal=false).
        Assert.Throws<System.Security.Cryptography.CryptographicException>(
            () => backend.DecryptChunk(chunk1, aad0));
    }

    [SkippableFact]
    public void CrossFileChunkSubstitution_ThrowsOnDecrypt()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "DPAPI is Windows-only");
        if (!OperatingSystem.IsWindows()) return; // CA1416 analyzer requires this; deliberate redundancy
        DpapiBackend backend = new(Scope.User);

        byte[] fileIdA = Header.NewFileId();
        byte[] fileIdB = Header.NewFileId();
        byte[] hdrA = Header.SerializeForAad(backend.Marker, fileIdA);
        byte[] hdrB = Header.SerializeForAad(backend.Marker, fileIdB);

        AadContext aadA = new(hdrA, 0, true);
        AadContext aadB = new(hdrB, 0, true);

        byte[] chunkFromB = backend.EncryptChunk([0xAA, 0xBB], aadB, isFinal: true);

        Assert.Throws<System.Security.Cryptography.CryptographicException>(
            () => backend.DecryptChunk(chunkFromB, aadA));
    }
}
