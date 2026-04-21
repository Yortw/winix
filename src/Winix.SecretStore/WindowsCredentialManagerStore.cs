#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Winix.SecretStore;

/// <summary>
/// Windows Credential Manager backend. Uses the classic Win32 Credential Management API
/// (<c>CredReadW</c>/<c>CredWriteW</c>/<c>CredDeleteW</c> via <c>advapi32.dll</c>). Works from
/// unpackaged console apps (unlike WinRT <c>PasswordVault</c> which requires MSIX packaging).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsCredentialManagerStore : ISecretStore
{
    private const uint CRED_TYPE_GENERIC = 1;
    private const uint CRED_PERSIST_LOCAL_MACHINE = 2;
    private const int ERROR_NOT_FOUND = 1168;

    public void Set(string namespace_, string key, byte[] value)
    {
        string target = Compose(namespace_, key);
        IntPtr blobPtr = Marshal.AllocHGlobal(value.Length);
        try
        {
            Marshal.Copy(value, 0, blobPtr, value.Length);

            CREDENTIAL cred = new()
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = target,
                CredentialBlobSize = (uint)value.Length,
                CredentialBlob = blobPtr,
                Persist = CRED_PERSIST_LOCAL_MACHINE,
                UserName = Environment.UserName,
            };

            if (!CredWriteW(ref cred, 0))
            {
                int err = Marshal.GetLastWin32Error();
                throw new Win32Exception(err, $"CredWriteW failed for target '{target}' (0x{err:X}).");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(blobPtr);
        }
    }

    public byte[]? Get(string namespace_, string key)
    {
        string target = Compose(namespace_, key);
        if (!CredReadW(target, CRED_TYPE_GENERIC, 0, out IntPtr credPtr))
        {
            int err = Marshal.GetLastWin32Error();
            if (err == ERROR_NOT_FOUND)
            {
                return null;
            }
            throw new Win32Exception(err, $"CredReadW failed for target '{target}' (0x{err:X}).");
        }

        try
        {
            CREDENTIAL cred = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
            byte[] value = new byte[cred.CredentialBlobSize];
            Marshal.Copy(cred.CredentialBlob, value, 0, (int)cred.CredentialBlobSize);
            return value;
        }
        finally
        {
            CredFree(credPtr);
        }
    }

    public bool Delete(string namespace_, string key)
    {
        string target = Compose(namespace_, key);
        if (CredDeleteW(target, CRED_TYPE_GENERIC, 0))
        {
            return true;
        }

        int err = Marshal.GetLastWin32Error();
        if (err == ERROR_NOT_FOUND)
        {
            return false;
        }
        throw new Win32Exception(err, $"CredDeleteW failed for target '{target}' (0x{err:X}).");
    }

    public IReadOnlyList<string> ListKeys(string namespace_) =>
        throw new NotImplementedException("ListKeys is implemented in a subsequent commit (envvault Task 2).");

    public IReadOnlyList<string> ListNamespaces(string toolPrefix) =>
        throw new NotImplementedException("ListNamespaces is implemented in a subsequent commit (envvault Task 2).");

    private static string Compose(string namespace_, string key) => $"{namespace_}/{key}";

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWriteW(ref CREDENTIAL credential, uint flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredReadW(string target, uint type, uint reservedFlag, out IntPtr credential);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDeleteW(string target, uint type, uint reservedFlag);

    [DllImport("advapi32.dll", SetLastError = false)]
    private static extern void CredFree(IntPtr buffer);
}
