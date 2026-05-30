#nullable enable
using System;
using System.Collections.Generic;

namespace Winix.Trash;

/// <summary>Windows Recycle Bin backend (SHFileOperationW / $I metadata). Stub — fleshed out in Task 12.</summary>
internal sealed class WindowsRecycleBinBackend : ITrashBackend
{
    public TrashResult Trash(IReadOnlyList<string> paths) => throw new NotImplementedException();

    public IReadOnlyList<TrashedItem> List() => throw new NotImplementedException();

    public EmptyResult Empty() => throw new NotImplementedException();
}
