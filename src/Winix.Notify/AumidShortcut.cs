#nullable enable
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Winix.Notify;

/// <summary>
/// Ensures a per-user Start Menu shortcut exists with the given AppUserModelID,
/// which Windows requires before <c>ToastNotificationManager.CreateToastNotifier(aumid)</c>
/// will display anything. Idempotent — skips file rewrite if the shortcut already exists
/// (file presence check; we don't introspect the AUMID property for speed).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class AumidShortcut
{
    /// <summary>The reverse-domain AUMID for notify. Must match what WindowsToastBackend passes to CreateToastNotifier.</summary>
    public const string Aumid = "Yortw.Winix.Notify";

    /// <summary>The shortcut file name shown in Start Menu Programs.</summary>
    public const string ShortcutName = "Winix Notify.lnk";

    /// <summary>Idempotently create the shortcut. Returns true if the shortcut existed or was created; false on failure.</summary>
    public static bool EnsureExists()
    {
        try
        {
            string startMenu = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            string path = Path.Combine(startMenu, ShortcutName);
            if (File.Exists(path))
            {
                return true;
            }
            CreateShortcut(path);
            return File.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    private static void CreateShortcut(string path)
    {
        string exePath = Environment.ProcessPath ?? "notify.exe";

        // CoCreateInstance for IShellLinkW (CLSID_ShellLink). Direct P/Invoke avoids the
        // [RequiresUnreferencedCode] warnings that come with Type.GetTypeFromCLSID under AOT.
        Guid clsidShellLink = new("00021401-0000-0000-C000-000000000046");
        Guid iidShellLink = typeof(IShellLinkW).GUID;

        int hr = CoCreateInstance(ref clsidShellLink, IntPtr.Zero,
            CLSCTX_INPROC_SERVER, ref iidShellLink, out IntPtr shellLinkPtr);
        if (hr != 0 || shellLinkPtr == IntPtr.Zero)
        {
            throw new InvalidOperationException($"CoCreateInstance(ShellLink) failed: HRESULT 0x{hr:X8}");
        }

        try
        {
            object shellLink = Marshal.GetObjectForIUnknown(shellLinkPtr);
            try
            {
                var link = (IShellLinkW)shellLink;
                link.SetPath(exePath);
                link.SetArguments("");
                link.SetDescription("Winix Notify — desktop notifications and ntfy.sh push");

                // Set the System.AppUserModel.ID property on the shortcut.
                // PKEY: {9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3}, 5
                var propStore = (IPropertyStore)shellLink;
                var pkAumid = new PropertyKey(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
                using (var pv = new PropVariantString(Aumid))
                {
                    propStore.SetValue(ref pkAumid, ref pv.Variant);
                    propStore.Commit();
                }

                var persist = (IPersistFile)shellLink;
                persist.Save(path, true);
            }
            finally
            {
                Marshal.ReleaseComObject(shellLink);
            }
        }
        finally
        {
            Marshal.Release(shellLinkPtr);
        }
    }

    private const uint CLSCTX_INPROC_SERVER = 0x1;

    [DllImport("ole32.dll", PreserveSig = true)]
    private static extern int CoCreateInstance(
        [In] ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext,
        [In] ref Guid riid, out IntPtr ppv);

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cchMaxPath, IntPtr pfd, int fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0000010B-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, int dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    private interface IPropertyStore
    {
        void GetCount(out uint cProps);
        void GetAt(uint iProp, out PropertyKey pkey);
        void GetValue(ref PropertyKey key, out PropVariant pv);
        void SetValue(ref PropertyKey key, ref PropVariant pv);
        void Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public int PropertyId;
        public PropertyKey(Guid fmtid, int pid) { FormatId = fmtid; PropertyId = pid; }
    }

    // Minimal PropVariant for VT_LPWSTR strings only — covers the AUMID-set use case.
    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(2)] public ushort wReserved1;
        [FieldOffset(4)] public ushort wReserved2;
        [FieldOffset(6)] public ushort wReserved3;
        [FieldOffset(8)] public IntPtr pointerValue;
    }

    private const ushort VT_LPWSTR = 31;

    private sealed class PropVariantString : IDisposable
    {
        public PropVariant Variant;
        public PropVariantString(string s)
        {
            Variant.vt = VT_LPWSTR;
            Variant.pointerValue = Marshal.StringToCoTaskMemUni(s);
        }
        public void Dispose()
        {
            if (Variant.pointerValue != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(Variant.pointerValue);
                Variant.pointerValue = IntPtr.Zero;
            }
        }
    }
}
