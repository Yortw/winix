#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Winix.WhoHolds;

/// <summary>
/// Finds processes that hold a lock on a given file path using the Windows Restart Manager API.
/// Does not require elevated privileges — Restart Manager was designed for this use case.
/// Only sees processes belonging to the current user session.
/// Returns an empty list on non-Windows platforms or when the API is unavailable.
/// </summary>
public static class FileLockFinder
{
    // ERROR_MORE_DATA: RmGetList signals that the buffer was too small; re-call with the
    // returned needed count. This is the documented two-step pattern for RmGetList.
    private const int ErrorMoreData = 234;

    /// <summary>
    /// Returns a list of processes currently holding a lock on <paramref name="filePath"/>.
    /// Returns an empty list if the file is not locked, does not exist, or if this method
    /// is called on a non-Windows platform.
    /// Never throws — API failures are silently swallowed and an empty list is returned.
    /// </summary>
    /// <param name="filePath">Absolute path to the file to query.</param>
    public static List<LockInfo> Find(string filePath)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new List<LockInfo>();
        }

        var results = new List<LockInfo>();
        uint sessionHandle = 0;

        try
        {
            int hr = RmStartSession(out sessionHandle, 0, Guid.NewGuid().ToString());
            if (hr != 0)
            {
                return results;
            }

            hr = RmRegisterResources(sessionHandle, 1, new[] { filePath }, 0, null, 0, null);
            if (hr != 0)
            {
                return results;
            }

            uint needed = 0;
            uint count = 0;
            uint rebootReasons = 0;

            // First call: retrieve the required array size. ERROR_MORE_DATA (234) is the
            // expected return when the output array is null — it signals how many entries are needed.
            hr = RmGetList(sessionHandle, out needed, ref count, null, ref rebootReasons);
            if (hr != ErrorMoreData || needed == 0)
            {
                return results;
            }

            var processInfo = new RM_PROCESS_INFO[needed];
            count = needed;
            hr = RmGetList(sessionHandle, out needed, ref count, processInfo, ref rebootReasons);
            if (hr != 0)
            {
                return results;
            }

            for (uint i = 0; i < count; i++)
            {
                var info = processInfo[i];
                results.Add(new LockInfo(
                    (int)info.Process.dwProcessId,
                    info.strAppName ?? string.Empty,
                    filePath));
            }
        }
        catch
        {
            // Swallow all exceptions — callers should never crash because of a lock query.
        }
        finally
        {
            if (sessionHandle != 0)
            {
                RmEndSession(sessionHandle);
            }
        }

        return results;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RM_UNIQUE_PROCESS
    {
        public uint dwProcessId;
        public long ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string strAppName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string strServiceShortName;

        public uint ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;

        [MarshalAs(UnmanagedType.Bool)]
        public bool bRestartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint sessionHandle, int flags, string sessionKey);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(
        uint sessionHandle,
        uint nFiles,
        string[] files,
        uint nApps,
        RM_UNIQUE_PROCESS[]? apps,
        uint nServices,
        string[]? services);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(
        uint sessionHandle,
        out uint needed,
        ref uint count,
        [In, Out] RM_PROCESS_INFO[]? info,
        ref uint rebootReasons);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint sessionHandle);
}
