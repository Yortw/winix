#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Winix.WhoHolds;

/// <summary>
/// Finds processes that hold a lock on a given file path using the Windows Restart Manager API.
/// Does not require elevated privileges — Restart Manager was designed for this use case.
/// Only sees processes belonging to the current user session.
/// Returns an empty success result on non-Windows platforms.
/// </summary>
public static class FileLockFinder
{
    // ERROR_MORE_DATA: RmGetList signals that the buffer was too small; re-call with the
    // returned needed count. This is the documented two-step pattern for RmGetList.
    private const int ErrorMoreData = 234;

    // RM enumerates handles via the kernel object table and is eventually-consistent — a
    // file opened microseconds before RmStartSession can be missed on the first probe.
    // A short bounded retry handles same-process-recently-opened cases without affecting
    // the truly-unlocked path (which still returns empty after the retries exhaust).
    private const int MaxFindAttempts = 5;
    private const int FindRetryDelayMs = 50;

    /// <summary>
    /// Returns the lock-query outcome for <paramref name="filePath"/>.
    /// On non-Windows platforms returns <see cref="FindResult.Empty"/> (success-empty) —
    /// this method has no backend off-Windows. On Windows, returns a successful result
    /// with the processes (possibly empty) when the Restart Manager API path completed,
    /// or <see cref="FindResult.Failed"/> when an RM call returned a non-zero hr or an
    /// unexpected exception was thrown.
    /// </summary>
    /// <param name="filePath">Absolute path to the file to query.</param>
    public static FindResult Find(string filePath)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return FindResult.Empty;
        }

        // Bounded retry around the RM probe: under high concurrent load (or for a file
        // opened immediately before this call), RmGetList can return success-with-zero
        // entries even when a handle does exist in the kernel table. Each attempt opens
        // a fresh RM session — there is no API for "wait for consistency."
        // Retry only on empty success; backend failures surface immediately because they
        // are not transient consistency artefacts.
        FindResult last = FindResult.Empty;
        for (int attempt = 0; attempt < MaxFindAttempts; attempt++)
        {
            last = TryFind(filePath);
            if (last.QueryFailed || last.Results.Count > 0 || attempt == MaxFindAttempts - 1)
            {
                return last;
            }
            System.Threading.Thread.Sleep(FindRetryDelayMs);
        }

        return last;
    }

    private static FindResult TryFind(string filePath)
    {
        var results = new List<LockInfo>();
        uint sessionHandle = 0;

        try
        {
            int hr = RmStartSession(out sessionHandle, 0, Guid.NewGuid().ToString());
            if (hr != 0)
            {
                return FindResult.Failed(FormatHrFailure("RmStartSession", hr));
            }

            hr = RmRegisterResources(sessionHandle, 1, new[] { filePath }, 0, null, 0, null);
            if (hr != 0)
            {
                return FindResult.Failed(FormatHrFailure("RmRegisterResources", hr));
            }

            uint needed = 0;
            uint count = 0;
            uint rebootReasons = 0;

            // First call: retrieve the required array size. ERROR_MORE_DATA (234) is the
            // expected return when the output array is null — it signals how many entries are needed.
            hr = RmGetList(sessionHandle, out needed, ref count, null, ref rebootReasons);
            if (hr == 0 && needed == 0)
            {
                // RM reports zero handles cleanly — success-empty.
                return FindResult.Success(results);
            }
            if (hr != ErrorMoreData)
            {
                return FindResult.Failed(FormatHrFailure("RmGetList (probe)", hr));
            }
            if (needed == 0)
            {
                // ERROR_MORE_DATA with needed==0 is a contradiction in the documented
                // contract; defensive — surface as success-empty rather than spinning a
                // zero-length call.
                return FindResult.Success(results);
            }

            var processInfo = new RM_PROCESS_INFO[needed];
            count = needed;
            hr = RmGetList(sessionHandle, out needed, ref count, processInfo, ref rebootReasons);
            if (hr != 0)
            {
                return FindResult.Failed(FormatHrFailure("RmGetList (fetch)", hr));
            }

            for (uint i = 0; i < count; i++)
            {
                var info = processInfo[i];
                int pid = (int)info.Process.dwProcessId;

                string processPath = string.Empty;
                try
                {
                    // MainModule requires same-user or elevated access; failures are expected
                    // for system processes and are intentionally swallowed here — the lookup
                    // is a best-effort enrichment, not part of the find contract.
                    processPath = Process.GetProcessById(pid).MainModule?.FileName ?? string.Empty;
                }
                catch
                {
                    // Access denied or process exited — path stays empty.
                }

                results.Add(new LockInfo(
                    pid,
                    info.strAppName ?? string.Empty,
                    filePath,
                    processPath));
            }

            return FindResult.Success(results);
        }
        catch (Exception ex)
        {
            // Any unexpected exception from the marshaller / RM DLLs surfaces as a failed
            // query — not an "empty list" lie. Swallowing was the SFH I1 silent-failure bug.
            return FindResult.Failed($"Restart Manager query crashed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            if (sessionHandle != 0)
            {
                RmEndSession(sessionHandle);
            }
        }
    }

    private static string FormatHrFailure(string apiName, int hr)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "Restart Manager API {0} failed: hr=0x{1:X8} ({2}).",
            apiName,
            hr,
            hr);
    }

    // The C definition uses FILETIME — two DWORD fields, total 8 bytes, 4-byte aligned.
    // Using `long` here would force 8-byte alignment on x64, inserting 4 bytes of padding
    // after dwProcessId and shifting every subsequent field of RM_PROCESS_INFO by 4 bytes
    // (= 2 wide chars). Result: the process name read from strAppName started 2 chars in,
    // producing names like "ndows PowerShell" instead of "Windows PowerShell".
    // Tier-2 baseline 2026-05-06 finding F1 — observed empirically before fix.
    [StructLayout(LayoutKind.Sequential)]
    private struct RM_UNIQUE_PROCESS
    {
        public uint dwProcessId;
        public uint dwLowDateTime;
        public uint dwHighDateTime;
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
