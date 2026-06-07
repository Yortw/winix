#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Winix.Trash;

/// <summary>Windows Recycle Bin backend. Trashing uses <c>SHFileOperationW</c> with
/// <c>FOF_ALLOWUNDO</c> (the flag that recycles rather than permanently deletes); listing parses the
/// per-drive <c>$Recycle.Bin\&lt;SID&gt;\$I*</c> metadata files; emptying uses
/// <c>SHEmptyRecycleBinW</c>. Per-path operational failures are recorded as
/// <see cref="PathOutcome"/>s and never thrown.</summary>
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsRecycleBinBackend : ITrashBackend
{
    /// <inheritdoc/>
    public TrashResult Trash(IReadOnlyList<string> paths)
    {
        // F2: call SHFileOperationW once PER PATH. A single batched call returns one code that
        // cannot be attributed to a specific path, so a batch failure would falsely mark the
        // already-recycled paths as failed.
        var outcomes = new List<PathOutcome>(paths.Count);
        foreach (string input in paths)
        {
            outcomes.Add(TrashOne(input));
        }

        return new TrashResult { Outcomes = outcomes };
    }

    private static PathOutcome TrashOne(string input)
    {
        string fullPath;
        try
        {
            // Canonicalise. Beyond the guard, a FULL path is required for FOF_ALLOWUNDO to recycle
            // at all — SHFileOperationW permanently deletes an unqualified name even with the flag
            // set (MS Learn, Remarks).
            fullPath = Path.GetFullPath(input);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new PathOutcome(input, "invalid path.");
        }

        // F1 trash-root guard: never feed the API a drive root or anything inside a $Recycle.Bin.
        if (TrashGuards.IsWindowsRefusedRoot(fullPath))
        {
            return new PathOutcome(input, "refusing to trash a drive root or the recycle bin itself.");
        }

        // Probe existence ourselves: SHFileOperationW on a missing path returns an opaque code, so
        // we give a clear "not found" instead.
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            return new PathOutcome(input, "not found.");
        }

        char[] from = BuildDoubleNullBuffer(fullPath);
        GCHandle pin = GCHandle.Alloc(from, GCHandleType.Pinned);
        try
        {
            var op = new SHFILEOPSTRUCTW
            {
                hwnd = IntPtr.Zero,
                wFunc = FO_DELETE,
                pFrom = pin.AddrOfPinnedObject(),
                pTo = IntPtr.Zero,
                fFlags = RecycleFlags,
                fAnyOperationsAborted = 0,
                hNameMappings = IntPtr.Zero,
                lpszProgressTitle = IntPtr.Zero,
            };

            int rc = SHFileOperationW(ref op);
            if (rc != 0)
            {
                // FOF_NOERRORUI suppresses the shell dialog, so the raw return code is the ONLY
                // diagnostic we get (F14). Surface it.
                return new PathOutcome(input, $"SHFileOperation failed (0x{rc:X}).");
            }

            if (op.fAnyOperationsAborted != 0)
            {
                return new PathOutcome(input, "operation aborted.");
            }

            return new PathOutcome(input, null);
        }
        finally
        {
            pin.Free();
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<TrashedItem> List()
    {
        var items = new List<TrashedItem>();

        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            bool ready;
            try
            {
                ready = drive.DriveType == DriveType.Fixed && drive.IsReady;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (!ready)
            {
                continue;
            }

            string recycleRoot = Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin");
            string driveLabel = drive.Name.TrimEnd('\\', '/'); // e.g. "C:"

            // Enumerate each <SID> subfolder rather than resolving the current user's SID via
            // WindowsIdentity — folder enumeration is AOT/trim-clean (no reflection) and naturally
            // skips SID folders we can't read (access-denied), which is exactly the desired
            // behaviour for a current-user listing.
            string[] sidDirs;
            try
            {
                if (!Directory.Exists(recycleRoot)) { continue; }
                sidDirs = Directory.GetDirectories(recycleRoot);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string sidDir in sidDirs)
            {
                CollectFromSidDir(sidDir, driveLabel, items);
            }
        }

        return items;
    }

    /// <summary>Reads every <c>$I*</c> metadata file under one SID folder, skipping corrupt/non-v2
    /// entries (F9/F17) and any file/dir that throws access-denied. Never throws.</summary>
    private static void CollectFromSidDir(string sidDir, string driveLabel, List<TrashedItem> items)
    {
        string[] infoFiles;
        try
        {
            infoFiles = Directory.GetFiles(sidDir, "$I*");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (string infoFile in infoFiles)
        {
            TrashedItem? item = TryReadEntry(infoFile, driveLabel);
            if (item is not null)
            {
                items.Add(item);
            }
        }
    }

    /// <summary>Parses one <c>$I</c> file into a <see cref="TrashedItem"/>, or null to skip it
    /// (short/non-v2/corrupt metadata, or an unreadable file). The display <c>Name</c> is the
    /// sibling <c>$R</c> entry's on-disk leaf name (what the recycled payload is actually called on
    /// disk), which is stable and matches what other recycle-bin tools surface.</summary>
    private static TrashedItem? TryReadEntry(string infoFile, string driveLabel)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(infoFile);
            RecycleEntry? entry = RecycleMetadata.TryParseIFile(bytes);
            if (entry is null)
            {
                return null; // F9/F17: short, non-v2, or otherwise unparseable — skip.
            }

            // The $R sibling shares the name with $I → $R substituted on the leaf.
            string leaf = Path.GetFileName(infoFile);
            string rLeaf = leaf.StartsWith("$I", StringComparison.Ordinal)
                ? "$R" + leaf.Substring(2)
                : leaf;

            return new TrashedItem(
                rLeaf,
                entry.OriginalPath,
                entry.DeletedUtc,
                entry.SizeBytes,
                driveLabel);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public EmptyResult Empty()
    {
        // F6: count via List() first. This is APPROXIMATE — SHEmptyRecycleBinW(null) clears every
        // bin on every drive, including SID folders or drives List() could not enumerate
        // (access-denied), so the returned count can understate what was actually removed.
        int count = List().Count;

        int hr = SHEmptyRecycleBinW(IntPtr.Zero, null, EmptyFlags);

        // S_OK (0) is success. An already-empty bin reports a benign code on some Windows versions
        // (E_UNEXPECTED 0x8000FFFF), which we tolerate so emptying an empty bin is not an error. Any
        // other non-success is a real backend failure → throw (Cli maps to exit 126); the raw HRESULT
        // is the only diagnostic.
        if (hr != 0 && unchecked((uint)hr) != 0x8000FFFF)
        {
            throw new TrashException($"SHEmptyRecycleBin failed (0x{hr:X8}).");
        }

        return new EmptyResult(count);
    }
}
