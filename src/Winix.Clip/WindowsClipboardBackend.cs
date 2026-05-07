using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Winix.Clip;

/// <summary>
/// Native Windows clipboard backend using user32 P/Invoke. Only functional on
/// Windows; constructing on another OS succeeds but operations throw
/// <see cref="PlatformNotSupportedException"/>.
/// </summary>
public sealed class WindowsClipboardBackend : IClipboardBackend
{
    // ReSharper disable InconsistentNaming
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;
    // ReSharper restore InconsistentNaming

    /// <inheritdoc />
    public void CopyText(string text)
    {
        EnsureWindows();
        ArgumentNullException.ThrowIfNull(text);

        // Pre-allocate and populate the unmanaged buffer BEFORE touching the clipboard.
        // The previous order (OpenClipboard → EmptyClipboard → GlobalAlloc → ...) meant
        // that an alloc failure on a memory-pressured host left the user's clipboard
        // truly empty (EmptyClipboard had already succeeded) with nothing replacing it.
        // Now alloc happens first; if it fails the clipboard is untouched.
        // Null-terminated UTF-16LE payload — CF_UNICODETEXT requires a trailing 0 WCHAR.
        int byteCount = (text.Length + 1) * sizeof(char);
        IntPtr hMem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)byteCount);
        if (hMem == IntPtr.Zero)
        {
            throw new ClipboardException("GlobalAlloc failed", LastWin32());
        }

        try
        {
            IntPtr target = GlobalLock(hMem);
            if (target == IntPtr.Zero)
            {
                throw new ClipboardException("GlobalLock failed", LastWin32());
            }

            try
            {
                Marshal.Copy(text.ToCharArray(), 0, target, text.Length);
                // Write the trailing \0 WCHAR.
                Marshal.WriteInt16(target, text.Length * sizeof(char), 0);
            }
            finally
            {
                GlobalUnlock(hMem);
            }

            // Buffer is fully populated. NOW open + claim ownership + transfer.
            using var scope = OpenScope();

            if (!EmptyClipboard())
            {
                // Open succeeded but EmptyClipboard failed — the prior owner still
                // owns; clipboard is untouched. Free our buffer in the outer finally.
                throw new ClipboardException("EmptyClipboard failed", LastWin32());
            }

            // After this point the user's previous clipboard contents are gone. If
            // SetClipboardData fails, we cannot restore them — annotate the error
            // so the operator knows the destructive side-effect already landed.
            if (SetClipboardData(CF_UNICODETEXT, hMem) == IntPtr.Zero)
            {
                throw new ClipboardException(
                    "SetClipboardData failed (clipboard was emptied; previous contents are lost)",
                    LastWin32());
            }

            // Ownership transferred to the system — do not GlobalFree on success.
            hMem = IntPtr.Zero;
        }
        finally
        {
            if (hMem != IntPtr.Zero)
            {
                GlobalFree(hMem);
            }
        }
    }

    /// <inheritdoc />
    public string PasteText()
    {
        EnsureWindows();
        using var scope = OpenScope();

        if (!IsClipboardFormatAvailable(CF_UNICODETEXT))
        {
            return string.Empty;
        }

        IntPtr hData = GetClipboardData(CF_UNICODETEXT);
        if (hData == IntPtr.Zero)
        {
            return string.Empty;
        }

        IntPtr src = GlobalLock(hData);
        if (src == IntPtr.Zero)
        {
            throw new ClipboardException("GlobalLock failed", LastWin32());
        }

        try
        {
            return Marshal.PtrToStringUni(src) ?? string.Empty;
        }
        finally
        {
            GlobalUnlock(hData);
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        EnsureWindows();
        using var scope = OpenScope();

        if (!EmptyClipboard())
        {
            throw new ClipboardException("EmptyClipboard failed", LastWin32());
        }
    }

    private static void EnsureWindows()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new PlatformNotSupportedException("WindowsClipboardBackend is only supported on Windows.");
        }
    }

    private static ClipboardScope OpenScope()
    {
        if (ClipboardRetryPolicy.TryOpenWithRetry(
            tryOpen: () => OpenClipboard(IntPtr.Zero),
            sleep: Thread.Sleep,
            attemptsMade: out _))
        {
            return new ClipboardScope();
        }

        throw new ClipboardException(
            "clipboard busy (another process holds it)",
            LastWin32());
    }

    private static Exception LastWin32() => new Win32Exception(Marshal.GetLastWin32Error());

    private readonly struct ClipboardScope : IDisposable
    {
        public void Dispose() => CloseClipboard();
    }

    // --- P/Invoke ---

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);
}
