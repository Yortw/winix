#nullable enable
using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Winix.Trash;

// P/Invoke layer for the Windows Recycle Bin backend. Source-generated [LibraryImport] (not
// [DllImport]) so the marshalling stubs are AOT/trim-safe.
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsRecycleBinBackend
{
    // SHFileOperationW wFunc values.
    private const uint FO_DELETE = 0x0003;

    // SHFileOperationW fFlags (a WORD / ushort).
    // FOF_ALLOWUNDO is what routes the delete to the Recycle Bin instead of permanently destroying
    // the file — getting this wrong is data loss, so all four flags below are always set together.
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;
    private const ushort RecycleFlags = FOF_SILENT | FOF_NOCONFIRMATION | FOF_ALLOWUNDO | FOF_NOERRORUI;

    // SHEmptyRecycleBinW dwFlags.
    private const uint SHERB_NOCONFIRMATION = 0x00000001;
    private const uint SHERB_NOPROGRESSUI = 0x00000002;
    private const uint SHERB_NOSOUND = 0x00000004;
    private const uint EmptyFlags = SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND;

    /// <summary>Matches the Win32 <c>SHFILEOPSTRUCTW</c> layout (shellapi.h). Field order and types
    /// are pinned against MS Learn:
    /// <c>hwnd</c> (HWND), <c>wFunc</c> (UINT), <c>pFrom</c> (PCZZWSTR), <c>pTo</c> (PCZZWSTR),
    /// <c>fFlags</c> (FILEOP_FLAGS=WORD), <c>fAnyOperationsAborted</c> (BOOL),
    /// <c>hNameMappings</c> (LPVOID), <c>lpszProgressTitle</c> (PCWSTR).
    /// <para><c>pFrom</c>/<c>pTo</c> are declared as <see cref="nint"/> and point at a manually-built,
    /// pinned native buffer (a double-null-terminated wide string). They are NOT declared as managed
    /// <c>string</c>: the source-gen marshaller would treat a string as single-null-terminated and
    /// truncate at the first embedded <c>\0</c>, breaking the required double-null form. This is a
    /// Win32 struct field, NOT a child-process argument list — the CLAUDE.md
    /// <c>ProcessStartInfo.ArgumentList</c> rule does not apply here.</para></summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCTW
    {
        public nint hwnd;
        public uint wFunc;
        public nint pFrom;
        public nint pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted; // BOOL
        public nint hNameMappings;
        public nint lpszProgressTitle;
    }

    [LibraryImport("shell32", EntryPoint = "SHFileOperationW")]
    private static partial int SHFileOperationW(ref SHFILEOPSTRUCTW lpFileOp);

    [LibraryImport("shell32", EntryPoint = "SHEmptyRecycleBinW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHEmptyRecycleBinW(nint hwnd, string? pszRootPath, uint dwFlags);

    /// <summary>Builds the double-null-terminated wide buffer SHFileOperationW expects for a single
    /// path: <c>path</c> + one terminator for the path + one extra terminator for the list. Pure and
    /// allocation-explicit so it can be unit-tested without invoking the shell.</summary>
    internal static char[] BuildDoubleNullBuffer(string singlePath)
    {
        // path chars + path terminator '\0' + list terminator '\0'.
        char[] buffer = new char[singlePath.Length + 2];
        singlePath.CopyTo(0, buffer, 0, singlePath.Length);
        buffer[singlePath.Length] = '\0';
        buffer[singlePath.Length + 1] = '\0';
        return buffer;
    }
}
