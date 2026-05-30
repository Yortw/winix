#nullable enable
using System;
using System.Collections.Generic;

namespace Winix.Trash;

/// <summary>FreeDesktop.org Trash-spec backend (Linux). Stub — fleshed out in Task 9/10.</summary>
internal sealed class LinuxFreeDesktopBackend : ITrashBackend
{
    public TrashResult Trash(IReadOnlyList<string> paths) => throw new NotImplementedException();

    public IReadOnlyList<TrashedItem> List() => throw new NotImplementedException();

    public EmptyResult Empty() => throw new NotImplementedException();
}
