#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;

namespace Winix.WhoHolds;

/// <summary>
/// Finds processes that hold a binding on a given TCP or UDP port using the Windows
/// IP Helper API (iphlpapi.dll). Does not require elevated privileges.
/// Returns an empty list on non-Windows platforms.
/// </summary>
public static class PortLockFinder
{
    // Address family constants for GetExtendedTcpTable / GetExtendedUdpTable.
    private const uint AF_INET = 2;
    private const uint AF_INET6 = 23;

    // Table class: TCP_TABLE_OWNER_PID_ALL (5) returns all TCP connections with owner PID.
    private const int TCP_TABLE_OWNER_PID_ALL = 5;

    // Table class: UDP_TABLE_OWNER_PID (1) returns all UDP listeners with owner PID.
    private const int UDP_TABLE_OWNER_PID = 1;

    // GetExtendedTcpTable / GetExtendedUdpTable return this when the supplied buffer is
    // too small — the required size is written to the size parameter.
    private const uint ERROR_INSUFFICIENT_BUFFER = 122;

    /// <summary>
    /// Returns all processes currently bound to <paramref name="port"/> on any protocol
    /// (TCP IPv4, TCP IPv6, UDP IPv4, UDP IPv6).
    /// Returns an empty list when not running on Windows.
    /// Never throws — API failures are silently swallowed.
    /// </summary>
    /// <param name="port">Port number to query (1–65535).</param>
    public static List<LockInfo> Find(int port)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new List<LockInfo>();
        }

        var results = new List<LockInfo>();

        // Deduplicate across the four tables — a process binding on :: also appears in IPv4
        // tables on dual-stack stacks, so the same PID can show up multiple times.
        var seen = new HashSet<int>();

        try
        {
            ScanTcpTable(port, AF_INET, results, seen);
            ScanTcpTable(port, AF_INET6, results, seen);
            ScanUdpTable(port, AF_INET, results, seen);
            ScanUdpTable(port, AF_INET6, results, seen);
        }
        catch
        {
            // Swallow all exceptions — callers must never crash because of a port query.
        }

        return results;
    }

    /// <summary>
    /// Scans a TCP owner-PID table (IPv4 or IPv6) and appends matching entries to
    /// <paramref name="results"/>, deduplicating via <paramref name="seen"/>.
    /// </summary>
    private static void ScanTcpTable(int port, uint af, List<LockInfo> results, HashSet<int> seen)
    {
        uint size = 0;

        // First call with a null buffer obtains the required buffer size.
        uint ret = GetExtendedTcpTable(IntPtr.Zero, ref size, false, af, TCP_TABLE_OWNER_PID_ALL, 0);
        if (ret != ERROR_INSUFFICIENT_BUFFER || size == 0)
        {
            return;
        }

        IntPtr tablePtr = Marshal.AllocHGlobal((int)size);
        try
        {
            ret = GetExtendedTcpTable(tablePtr, ref size, false, af, TCP_TABLE_OWNER_PID_ALL, 0);
            if (ret != 0)
            {
                return;
            }

            int numEntries = Marshal.ReadInt32(tablePtr);

            // Rows begin immediately after the leading numEntries int (4 bytes).
            IntPtr rowPtr = tablePtr + 4;

            if (af == AF_INET)
            {
                int rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();
                for (int i = 0; i < numEntries; i++)
                {
                    var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                    int rowPort = NetworkPortToHostOrder(row.dwLocalPort);
                    if (rowPort == port)
                    {
                        AddResult(results, seen, (int)row.dwOwningPid, "TCP", port);
                    }
                    rowPtr += rowSize;
                }
            }
            else
            {
                int rowSize = Marshal.SizeOf<MIB_TCP6ROW_OWNER_PID>();
                for (int i = 0; i < numEntries; i++)
                {
                    var row = Marshal.PtrToStructure<MIB_TCP6ROW_OWNER_PID>(rowPtr);
                    int rowPort = NetworkPortToHostOrder(row.dwLocalPort);
                    if (rowPort == port)
                    {
                        AddResult(results, seen, (int)row.dwOwningPid, "TCP", port);
                    }
                    rowPtr += rowSize;
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(tablePtr);
        }
    }

    /// <summary>
    /// Scans a UDP owner-PID table (IPv4 or IPv6) and appends matching entries to
    /// <paramref name="results"/>, deduplicating via <paramref name="seen"/>.
    /// </summary>
    private static void ScanUdpTable(int port, uint af, List<LockInfo> results, HashSet<int> seen)
    {
        uint size = 0;

        uint ret = GetExtendedUdpTable(IntPtr.Zero, ref size, false, af, UDP_TABLE_OWNER_PID, 0);
        if (ret != ERROR_INSUFFICIENT_BUFFER || size == 0)
        {
            return;
        }

        IntPtr tablePtr = Marshal.AllocHGlobal((int)size);
        try
        {
            ret = GetExtendedUdpTable(tablePtr, ref size, false, af, UDP_TABLE_OWNER_PID, 0);
            if (ret != 0)
            {
                return;
            }

            int numEntries = Marshal.ReadInt32(tablePtr);

            IntPtr rowPtr = tablePtr + 4;

            if (af == AF_INET)
            {
                int rowSize = Marshal.SizeOf<MIB_UDPROW_OWNER_PID>();
                for (int i = 0; i < numEntries; i++)
                {
                    var row = Marshal.PtrToStructure<MIB_UDPROW_OWNER_PID>(rowPtr);
                    int rowPort = NetworkPortToHostOrder(row.dwLocalPort);
                    if (rowPort == port)
                    {
                        AddResult(results, seen, (int)row.dwOwningPid, "UDP", port);
                    }
                    rowPtr += rowSize;
                }
            }
            else
            {
                int rowSize = Marshal.SizeOf<MIB_UDP6ROW_OWNER_PID>();
                for (int i = 0; i < numEntries; i++)
                {
                    var row = Marshal.PtrToStructure<MIB_UDP6ROW_OWNER_PID>(rowPtr);
                    int rowPort = NetworkPortToHostOrder(row.dwLocalPort);
                    if (rowPort == port)
                    {
                        AddResult(results, seen, (int)row.dwOwningPid, "UDP", port);
                    }
                    rowPtr += rowSize;
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(tablePtr);
        }
    }

    /// <summary>
    /// Converts a port value from network byte order to host byte order.
    /// The IP Helper API stores port numbers in network (big-endian) byte order.
    /// </summary>
    private static int NetworkPortToHostOrder(uint networkPort)
    {
        return (ushort)IPAddress.NetworkToHostOrder((short)(networkPort & 0xFFFF));
    }

    /// <summary>
    /// Looks up the process name for <paramref name="pid"/> and appends a <see cref="LockInfo"/>
    /// to <paramref name="results"/> if the PID has not already been added (tracked via
    /// <paramref name="seen"/>).
    /// </summary>
    private static void AddResult(List<LockInfo> results, HashSet<int> seen, int pid, string protocol, int port)
    {
        if (!seen.Add(pid))
        {
            return;
        }

        string processName = string.Empty;
        try
        {
            processName = Process.GetProcessById(pid).ProcessName;
        }
        catch
        {
            // Process may have exited or access may be denied — leave name empty.
        }

        results.Add(new LockInfo(pid, processName, $"{protocol} :{port}"));
    }

    // -------------------------------------------------------------------------
    // P/Invoke structs
    // -------------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW_OWNER_PID
    {
        public uint dwState;
        public uint dwLocalAddr;
        public uint dwLocalPort;
        public uint dwRemoteAddr;
        public uint dwRemotePort;
        public uint dwOwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCP6ROW_OWNER_PID
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] ucLocalAddr;

        public uint dwLocalScopeId;
        public uint dwLocalPort;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] ucRemoteAddr;

        public uint dwRemoteScopeId;
        public uint dwRemotePort;
        public uint dwState;
        public uint dwOwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_UDPROW_OWNER_PID
    {
        public uint dwLocalAddr;
        public uint dwLocalPort;
        public uint dwOwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_UDP6ROW_OWNER_PID
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] ucLocalAddr;

        public uint dwLocalScopeId;
        public uint dwLocalPort;
        public uint dwOwningPid;
    }

    // -------------------------------------------------------------------------
    // P/Invoke declarations
    // -------------------------------------------------------------------------

    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable,
        ref uint pdwSize,
        bool bOrder,
        uint ulAf,
        int tableClass,
        uint reserved);

    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedUdpTable(
        IntPtr pUdpTable,
        ref uint pdwSize,
        bool bOrder,
        uint ulAf,
        int tableClass,
        uint reserved);
}
